using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AudioMixer.Models;
using AudioMixer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioMixer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly AppConfig _config;
    private readonly IconService _icons = new();
    private readonly AudioService _audio;
    private readonly SerialService _serial = new();
    private readonly DeviceDetector _detector = new();
    private readonly FirmwareService _firmware = new();

    private string _lastSessionSignature = "\0";

    private readonly DispatcherTimer _sessionTimer;
    private readonly DispatcherTimer _reconnectTimer;
    private readonly SemaphoreSlim _detectGate = new(1, 1);

    public ObservableCollection<FaderViewModel> Faders { get; } = new();
    public ObservableCollection<AudioSessionInfo> Sessions { get; } = new();

    /// <summary>Targets offered by every fader's picker (wildcards + live sessions + assigned-but-closed apps).</summary>
    public ObservableCollection<AssignmentOption> AssignmentOptions { get; } = new();

    public ObservableCollection<SerialPortInfo> AvailablePorts { get; } = new();

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private string _deviceInfo = "No device";
    [ObservableProperty] private string _deviceName = "AudioMixer";
    [ObservableProperty] private int _hysteresis = 3;
    [ObservableProperty] private bool _autoDetect;
    [ObservableProperty] private SerialPortInfo? _selectedPort;
    [ObservableProperty] private string _baudRateText;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isCalibrating;
    [ObservableProperty] private bool _isFlashing;

    public MainWindowViewModel()
    {
        _config = ConfigService.Load();
        _audio = new AudioService(_icons);
        _autoDetect = _config.AutoDetect;
        _baudRateText = _config.BaudRate.ToString();

        BuildFaders(_config.Applications);
        RebuildAssignmentOptions();

        // Wire the hardware/audio services.
        _serial.PacketReceived += OnPacketReceived;
        _serial.IdentityReceived += OnIdentityReceived;
        _serial.ConnectionChanged += OnConnectionChanged;
        _detector.DevicesChanged += OnDevicesChanged;

        RefreshSessions();
        RefreshPorts();

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sessionTimer.Tick += (_, _) => RefreshSessions();
        _sessionTimer.Start();

        _reconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _reconnectTimer.Tick += async (_, _) => await EnsureConnectedAsync();
        _reconnectTimer.Start();

        _detector.StartWatching();

        // Kick off the first connection attempt.
        _ = EnsureConnectedAsync();
    }

    // ---- Fader construction -------------------------------------------------

    private void BuildFaders(IReadOnlyList<string> assignments)
    {
        Faders.Clear();
        for (int i = 0; i < assignments.Count; i++)
        {
            var fader = new FaderViewModel(i, assignments[i], AssignmentOptions);
            if (i < _config.Calibration.Count)
            {
                fader.CalMin = _config.Calibration[i].Min;
                fader.CalMax = _config.Calibration[i].Max;
            }
            Faders.Add(fader);
        }
    }

    /// <summary>Resizes the fader list to match the device's reported fader count.</summary>
    private void ResizeFaders(int count)
    {
        if (count <= 0 || count == Faders.Count)
            return;

        while (Faders.Count < count)
            Faders.Add(new FaderViewModel(Faders.Count, "", AssignmentOptions));
        while (Faders.Count > count)
            Faders.RemoveAt(Faders.Count - 1);

        RebuildAssignmentOptions();
    }

    // ---- Serial event handlers ---------------------------------------------

    private void OnPacketReceived(MixerPacket packet)
    {
        int channel = packet.Channel;
        if (channel < 0 || channel >= Faders.Count)
            return;

        double raw = packet.Percentage;

        // During calibration we only learn each slider's range, and we must NOT drive
        // audio (that would blast volume while sweeping the sliders).
        if (IsCalibrating)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (channel < Faders.Count)
                {
                    Faders[channel].ObserveCalibration(raw);
                    Faders[channel].Volume = raw;
                }
            });
            return;
        }

        double calibrated = Faders[channel].Calibrate(raw);

        // Audio work stays off the UI thread. Only the bound level hops onto it.
        var assignments = Faders.Select(f => f.Assignment).ToList();
        _audio.ApplyChannel(assignments, channel, (float)calibrated);

        Dispatcher.UIThread.Post(() =>
        {
            if (channel < Faders.Count)
                Faders[channel].Volume = calibrated;
        });
    }

    private void OnIdentityReceived(DeviceIdentity identity)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrWhiteSpace(identity.Device))
                DeviceName = identity.Device!;
            if (identity.Hysteresis > 0)
                Hysteresis = identity.Hysteresis;
            DeviceInfo = $"Firmware {identity.Firmware} · {identity.Faders} faders";
            ResizeFaders(identity.Faders);
        });
    }

    private void OnConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            if (connected)
            {
                ConnectionStatus = $"Connected · {_serial.PortName}";
            }
            else
            {
                ConnectionStatus = "Disconnected";
                DeviceInfo = "No device";
            }
        });
    }

    private void OnDevicesChanged()
    {
        // Fired on a WMI thread, so marshal onto the UI thread.
        Dispatcher.UIThread.Post(async () =>
        {
            RefreshPorts();
            await EnsureConnectedAsync();
        });
    }

    // ---- Connection orchestration ------------------------------------------

    private int CurrentBaud =>
        int.TryParse(BaudRateText, out int b) && b > 0 ? b : 9600;

    /// <summary>Connects if needed: auto-detects the mixer, or opens the chosen port.</summary>
    private async Task EnsureConnectedAsync()
    {
        if (IsFlashing)
            return; // never grab the port while avrdude is using it
        if (_serial.IsOpen)
            return;
        if (!await _detectGate.WaitAsync(0))
            return;

        try
        {
            if (AutoDetect)
            {
                StatusMessage = "Scanning for device…";
                var result = await _detector.DetectAsync(CurrentBaud);
                if (result != null)
                {
                    // Show the device's own name as soon as it's detected.
                    string name = result.Identity.Device ?? "device";
                    if (!string.IsNullOrWhiteSpace(result.Identity.Device))
                        DeviceName = result.Identity.Device!;
                    if (result.Identity.Hysteresis > 0)
                        Hysteresis = result.Identity.Hysteresis;
                    DeviceInfo = $"Firmware {result.Identity.Firmware} · {result.Identity.Faders} faders";

                    string? err = _serial.Connect(result.PortName, CurrentBaud);
                    StatusMessage = err == null
                        ? $"Found {name} on {result.PortName}."
                        : $"Found {name} but failed to open {result.PortName}: {err}";
                }
                else
                {
                    StatusMessage = "No mixer found on any port.";
                }
            }
            else
            {
                string? port = SelectedPort?.PortName ?? _config.COMPort;
                if (!string.IsNullOrWhiteSpace(port))
                {
                    string? err = _serial.Connect(port, CurrentBaud);
                    StatusMessage = err == null ? $"Connected to {port}." : $"Failed to open {port}: {err}";
                }
            }
        }
        finally
        {
            _detectGate.Release();
        }
    }

    // ---- Sessions / ports refresh ------------------------------------------

    private void RefreshSessions()
    {
        var sessions = _audio.RefreshSessions();

        // Skip all UI churn (and icon reloads) when the set of apps is unchanged.
        string signature = string.Join(";", sessions.Select(s => s.ProcessName));
        if (signature == _lastSessionSignature)
            return;
        _lastSessionSignature = signature;

        Sessions.Clear();
        foreach (var s in sessions)
            Sessions.Add(s);

        RebuildAssignmentOptions();
    }

    /// <summary>
    /// Rebuilds the shared picker options (wildcards + live sessions + any
    /// assigned-but-not-running apps) and re-resolves each fader's selection so
    /// the pickers keep showing the right target across refreshes.
    /// </summary>
    private void RebuildAssignmentOptions()
    {
        var options = new List<AssignmentOption>
        {
            new() { Value = "*", DisplayName = "Master volume", Glyph = "🔊" },
            new() { Value = "-", DisplayName = "All unassigned apps", Glyph = "🗂" },
            new() { Value = "!", DisplayName = "Active / fullscreen window", Glyph = "🎯" },
            new() { Value = "", DisplayName = "Unassigned", Glyph = "➖" },
        };

        foreach (var s in Sessions)
        {
            options.Add(new AssignmentOption
            {
                Value = s.ProcessName,
                DisplayName = s.ProcessName,
                Icon = s.Icon,
                IsRunning = true,
            });
        }

        // Keep an option for any app a fader points at that isn't currently running,
        // so saved assignments are never silently lost.
        foreach (var fader in Faders)
        {
            string a = fader.Assignment?.Trim() ?? "";
            if (a.Length == 0 || a is "*" or "-" or "!")
                continue;
            if (options.Any(o => o.Value.Equals(a, StringComparison.OrdinalIgnoreCase)))
                continue;

            options.Add(new AssignmentOption
            {
                Value = a,
                DisplayName = $"{a} (not running)",
                Glyph = "🎵",
                IsRunning = false,
            });
        }

        AssignmentOptions.Clear();
        foreach (var o in options)
            AssignmentOptions.Add(o);

        // Point each fader at its matching option instance in the shared list.
        foreach (var fader in Faders)
        {
            string a = fader.Assignment?.Trim() ?? "";
            fader.SelectedOption =
                AssignmentOptions.FirstOrDefault(o => o.Value.Equals(a, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void RefreshPorts()
    {
        var ports = _detector.ListPorts();
        string? previous = SelectedPort?.PortName ?? _config.COMPort;

        AvailablePorts.Clear();
        foreach (var p in ports)
            AvailablePorts.Add(p);

        SelectedPort = AvailablePorts.FirstOrDefault(p =>
                           string.Equals(p.PortName, previous, StringComparison.OrdinalIgnoreCase))
                       ?? AvailablePorts.FirstOrDefault();
    }

    // ---- Commands -----------------------------------------------------------

    [RelayCommand]
    private void Rescan() => RefreshSessions();

    [RelayCommand]
    private async Task Reconnect()
    {
        _serial.Disconnect();
        await EnsureConnectedAsync();
    }

    [RelayCommand]
    private void Save()
    {
        _config.Applications = Faders.Select(f => f.Assignment?.Trim() ?? "").ToList();
        _config.Calibration = Faders
            .Select(f => new SliderCalibration { Min = f.CalMin, Max = f.CalMax })
            .ToList();
        _config.AutoDetect = AutoDetect;
        _config.BaudRate = CurrentBaud;
        _config.Theme = "Dark";
        if (SelectedPort != null)
            _config.COMPort = SelectedPort.PortName;

        ConfigService.Save(_config, out string message);
        StatusMessage = message;
    }

    // ---- Calibration --------------------------------------------------------

    [RelayCommand]
    private void StartCalibration()
    {
        foreach (var f in Faders)
            f.BeginCalibration();
        IsCalibrating = true;
        StatusMessage = "Calibrating. Move every slider fully down, then fully up, then click Finish.";
    }

    [RelayCommand]
    private void FinishCalibration()
    {
        foreach (var f in Faders)
            f.CommitCalibration();
        IsCalibrating = false;
        Save();
        StatusMessage = "Calibration saved.";
    }

    [RelayCommand]
    private void CancelCalibration()
    {
        IsCalibrating = false;
        StatusMessage = "Calibration cancelled.";
    }

    // ---- Fader count --------------------------------------------------------

    private const int MaxFaders = 8;

    [RelayCommand]
    private void AddFader()
    {
        if (Faders.Count >= MaxFaders)
        {
            StatusMessage = $"Maximum of {MaxFaders} faders (ADC0-ADC7).";
            return;
        }

        int n = Faders.Count + 1;
        ResizeFaders(n);
        PushFaderCount(n, "Added fader");
    }

    [RelayCommand]
    private void RemoveFader()
    {
        if (Faders.Count <= 1)
            return;

        int n = Faders.Count - 1;
        ResizeFaders(n);
        PushFaderCount(n, "Removed fader");
    }

    // ---- Firmware flashing --------------------------------------------------

    [RelayCommand]
    private async Task FlashFirmware()
    {
        if (IsFlashing)
            return;

        string? port = _serial.IsOpen ? _serial.PortName : SelectedPort?.PortName;
        if (string.IsNullOrWhiteSpace(port))
        {
            StatusMessage = "No port to flash. Connect the device or pick a port in Manual mode.";
            return;
        }
        if (!_firmware.IsAvailable)
        {
            StatusMessage = "firmware.hex was not found next to the app.";
            return;
        }

        IsFlashing = true;
        try
        {
            StatusMessage = "Closing the connection to flash…";
            _serial.Disconnect();
            await Task.Delay(400); // let the port fully release

            bool ok = await _firmware.FlashAsync(port!,
                line => Dispatcher.UIThread.Post(() => StatusMessage = line));

            StatusMessage = ok ? "Firmware flashed. Reconnecting…" : "Firmware flashing failed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Flashing error: {ex.Message}";
        }
        finally
        {
            IsFlashing = false;
            await Task.Delay(600); // give the board time to reboot
            await EnsureConnectedAsync();
        }
    }

    // ---- Jitter filter (hysteresis) -----------------------------------------

    [RelayCommand]
    private void ApplyHysteresis()
    {
        int h = Math.Clamp(Hysteresis, 1, 64);
        Hysteresis = h;
        if (_serial.IsOpen)
        {
            _serial.SetHysteresis(h);
            StatusMessage = $"Jitter filter set to {h} (saved to the device).";
        }
        else
        {
            StatusMessage = "Connect the device to change the jitter filter.";
        }
    }

    // ---- Device name --------------------------------------------------------

    [RelayCommand]
    private void RenameDevice()
    {
        string name = (DeviceName ?? "").Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Enter a device name first.";
            return;
        }
        if (name.Length > 15)
        {
            name = name.Substring(0, 15);
            DeviceName = name;
        }

        if (_serial.IsOpen)
        {
            _serial.SetDeviceName(name);
            StatusMessage = $"Renamed device to \"{name}\" (saved to the device).";
        }
        else
        {
            StatusMessage = "Connect the device to save its name.";
        }
    }

    /// <summary>Persists the new fader count to the device (if connected) and reports it.</summary>
    private void PushFaderCount(int count, string action)
    {
        if (_serial.IsOpen)
        {
            _serial.SetFaderCount(count);
            StatusMessage = $"{action}. {count} faders saved to the device.";
        }
        else
        {
            StatusMessage = $"{action}. Connect the device to store {count} faders in its memory.";
        }
    }

    partial void OnAutoDetectChanged(bool value)
    {
        StatusMessage = value ? "Auto-detect enabled." : "Auto-detect disabled. Pick a port.";
        if (value && !_serial.IsOpen)
            _ = EnsureConnectedAsync();
    }

    public void Dispose()
    {
        _sessionTimer.Stop();
        _reconnectTimer.Stop();
        _serial.Dispose();
        _audio.Dispose();
        _detector.Dispose();
    }
}
