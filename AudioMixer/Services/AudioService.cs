using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Threading;
using AudioMixer.Models;
using NAudio.CoreAudioApi;

namespace AudioMixer.Services;

/// <summary>
/// Wraps NAudio's WASAPI (Core Audio) API to enumerate audio sessions and apply
/// per-application / master volumes. All session access is guarded and cached so
/// that high-frequency volume updates from the serial thread stay cheap and
/// never crash when a process exits mid-stream.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly object _lock = new();
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly IconService _icons;

    private MMDevice? _device;
    private AudioSessionManager? _sessionManager;

    // Cached snapshot of live sessions, rebuilt by RefreshSessions().
    private readonly List<CachedSession> _sessions = new();

    private sealed record CachedSession(int ProcessId, string ProcessName, AudioSessionControl Control);

    public AudioService(IconService icons)
    {
        _icons = icons;
        TryInitDevice();
    }

    private void TryInitDevice()
    {
        try
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _sessionManager = _device.AudioSessionManager;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioService] Device init failed: {ex.Message}");
            _device = null;
            _sessionManager = null;
        }
    }

    /// <summary>
    /// Rebuilds the cached session list from the current default render device.
    /// Returns a de-duplicated, UI-friendly view for the sessions list.
    /// </summary>
    public IReadOnlyList<AudioSessionInfo> RefreshSessions()
    {
        lock (_lock)
        {
            _sessions.Clear();

            // The default endpoint can change (headphones plugged in, etc.), so make
            // sure we are always looking at the current one.
            TryInitDevice();
            if (_sessionManager == null)
                return Array.Empty<AudioSessionInfo>();

            try
            {
                _sessionManager.RefreshSessions();
                var sessions = _sessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    try
                    {
                        int pid = (int)session.GetProcessID;
                        if (pid == 0)
                            continue; // system sounds

                        string name = Process.GetProcessById(pid).ProcessName;
                        _sessions.Add(new CachedSession(pid, name, session));
                    }
                    catch
                    {
                        // Process already gone, so skip this session.
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] RefreshSessions failed: {ex.Message}");
            }

            return _sessions
                .GroupBy(s => s.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    int pid = g.First().ProcessId;
                    string? exePath = ProcessHelper.GetExecutablePath(pid);
                    return new AudioSessionInfo
                    {
                        ProcessName = g.Key,
                        ProcessId = pid,
                        ExecutablePath = exePath,
                        Icon = _icons.GetIcon(exePath),
                    };
                })
                .OrderBy(s => s.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// Applies <paramref name="percent"/> (0-100) to the target assigned to
    /// <paramref name="channel"/> within <paramref name="assignments"/>.
    /// Handles the <c>*</c>, <c>-</c> and <c>!</c> wildcards as well as explicit
    /// process names (case-insensitive).
    /// </summary>
    public void ApplyChannel(IReadOnlyList<string> assignments, int channel, float percent)
    {
        if (channel < 0 || channel >= assignments.Count)
            return;

        string target = assignments[channel]?.Trim() ?? string.Empty;
        if (target.Length == 0)
            return;

        float scalar = Math.Clamp(percent / 100f, 0f, 1f);

        try
        {
            switch (target)
            {
                case "*":
                    SetMasterVolume(scalar);
                    break;
                case "-":
                    SetUnboundVolume(assignments, scalar);
                    break;
                case "!":
                    SetActiveWindowVolume(scalar);
                    break;
                default:
                    SetProcessVolume(target, scalar);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioService] ApplyChannel('{target}') failed: {ex.Message}");
        }
    }

    public void SetMasterVolume(float scalar)
    {
        // The endpoint master volume must be set on the UI/STA thread that owns
        // the device COM object. From the serial thread the call silently fails,
        // which is why the original WinForms driver used Invoke here.
        if (Dispatcher.UIThread.CheckAccess())
            SetMasterVolumeCore(scalar);
        else
            Dispatcher.UIThread.Post(() => SetMasterVolumeCore(scalar));
    }

    private void SetMasterVolumeCore(float scalar)
    {
        lock (_lock)
        {
            if (_device == null)
                return;
            try
            {
                if (Math.Abs(_device.AudioEndpointVolume.MasterVolumeLevelScalar - scalar) > 0.001f)
                    _device.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] SetMasterVolume failed: {ex.Message}");
            }
        }
    }

    private void SetProcessVolume(string processName, float scalar)
    {
        lock (_lock)
        {
            foreach (var session in _sessions.Where(s =>
                         s.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)))
            {
                SafeSetVolume(session.Control, scalar);
            }
        }
    }

    /// <summary>Applies the volume to every session not explicitly assigned elsewhere.</summary>
    private void SetUnboundVolume(IReadOnlyList<string> assignments, float scalar)
    {
        var bound = assignments
            .Where(a => !string.IsNullOrWhiteSpace(a) && a != "*" && a != "-" && a != "!")
            .Select(a => a.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            foreach (var session in _sessions.Where(s => !bound.Contains(s.ProcessName)))
            {
                SafeSetVolume(session.Control, scalar);
            }
        }
    }

    /// <summary>Applies the volume to the session owned by the foreground window.</summary>
    private void SetActiveWindowVolume(float scalar)
    {
        int pid = ActiveWindowService.GetForegroundProcessId();
        if (pid == 0)
            return;

        string? name = null;
        try
        {
            name = Process.GetProcessById(pid).ProcessName;
        }
        catch
        {
            // ignore
        }

        lock (_lock)
        {
            // Prefer an exact pid match, then fall back to any session owned by a
            // process of the same name (some apps play audio from a child process).
            var match = _sessions.FirstOrDefault(s => s.ProcessId == pid);
            if (match == null && name != null)
                match = _sessions.FirstOrDefault(s =>
                    s.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                SafeSetVolume(match.Control, scalar);
        }
    }

    private static void SafeSetVolume(AudioSessionControl control, float scalar)
    {
        try
        {
            if (Math.Abs(control.SimpleAudioVolume.Volume - scalar) > 0.001f)
                control.SimpleAudioVolume.Volume = scalar;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioService] SafeSetVolume failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            _sessionManager?.Dispose();
            _device?.Dispose();
            _enumerator.Dispose();
        }
        catch
        {
            // ignore
        }
    }
}
