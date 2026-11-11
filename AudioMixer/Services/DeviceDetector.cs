using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AudioMixer.Models;

namespace AudioMixer.Services;

/// <summary>Enumerated serial port plus any USB identity we could learn about it.</summary>
public sealed class SerialPortInfo
{
    public required string PortName { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? VendorId { get; init; }
    public string? ProductId { get; init; }

    /// <summary>True when the port's USB vendor matches a common USB-serial bridge.</summary>
    public bool IsLikelyDevice { get; init; }

    /// <summary>Bluetooth virtual COM ports can block for a long time on open, so they are never probed.</summary>
    public bool IsBluetooth { get; init; }

    public string Display =>
        string.IsNullOrWhiteSpace(Description) ? PortName : $"{PortName} - {Description}";
}

/// <summary>Result of a successful handshake probe.</summary>
public sealed record DetectionResult(string PortName, DeviceIdentity Identity);

/// <summary>
/// Finds the mixer on USB: lists serial ports (with USB VID/PID where possible),
/// confirms the real device by performing the <c>ID?</c> handshake, and watches
/// for hot-plug so the app can auto-reconnect.
/// </summary>
public sealed class DeviceDetector : IDisposable
{
    // USB vendor IDs of the common serial bridges used by ATmega328P boards.
    private static readonly HashSet<string> KnownVendorIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "1A86", // QinHeng CH340 / CH341 (most Nano clones)
        "0403", // FTDI
        "2341", // Arduino (genuine, ATmega16U2)
        "2A03", // Arduino.org
        "10C4", // Silicon Labs CP210x
        "067B", // Prolific PL2303
        "1B4F", // SparkFun
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex ComRegex = new(@"\((COM\d+)\)", RegexOptions.Compiled);
    private static readonly Regex VidPidRegex =
        new(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private ManagementEventWatcher? _watcher;

    // Ports with a probe still running. A port can only be opened once, so a new
    // scan must not try a port an older (possibly stuck) probe still holds.
    private static readonly ConcurrentDictionary<string, byte> Probing = new(StringComparer.OrdinalIgnoreCase);

    // Upper bound for a whole scan, so one unresponsive port can't stall detection.
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Raised (on a background thread) when a USB device is plugged in or removed.</summary>
    public event Action? DevicesChanged;

    /// <summary>Lists available serial ports, enriched with USB descriptions via WMI when available.</summary>
    public List<SerialPortInfo> ListPorts()
    {
        var byName = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (ManagementObject obj in searcher.Get())
            {
                string? name = obj["Name"]?.ToString();
                string? pnp = obj["PNPDeviceID"]?.ToString();
                if (name == null)
                    continue;

                var m = ComRegex.Match(name);
                if (!m.Success)
                    continue;

                string port = m.Groups[1].Value;
                string? vid = null, pid = null;
                if (pnp != null)
                {
                    var vp = VidPidRegex.Match(pnp);
                    if (vp.Success)
                    {
                        vid = vp.Groups[1].Value.ToUpperInvariant();
                        pid = vp.Groups[2].Value.ToUpperInvariant();
                    }
                }

                byName[port] = new SerialPortInfo
                {
                    PortName = port,
                    Description = name,
                    VendorId = vid,
                    ProductId = pid,
                    IsLikelyDevice = vid != null && KnownVendorIds.Contains(vid),
                    IsBluetooth = (pnp?.StartsWith("BTHENUM", StringComparison.OrdinalIgnoreCase) ?? false)
                                  || name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase),
                };
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceDetector] WMI enumeration failed: {ex.Message}");
        }

        // Make sure every port .NET can see is represented, even if WMI missed it.
        foreach (string port in SerialPort.GetPortNames())
        {
            if (!byName.ContainsKey(port))
                byName[port] = new SerialPortInfo { PortName = port };
        }

        return byName.Values
            .OrderByDescending(p => p.IsLikelyDevice)
            .ThenBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Probes available ports (likely USB-serial ones first) and returns the first
    /// that answers the handshake. <paramref name="excludePort"/> skips a port that
    /// is already in use by the live connection.
    /// </summary>
    public async Task<DetectionResult?> DetectAsync(
        int baudRate, string? excludePort = null, CancellationToken ct = default)
    {
        var ports = ListPorts()
            .Where(p => !p.IsBluetooth)
            .Where(p => !Probing.ContainsKey(p.PortName))
            .Where(p => !string.Equals(p.PortName, excludePort, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (ports.Count == 0)
            return null;

        var timeout = Task.Delay(ScanTimeout, ct);

        // Probe all candidates in parallel and return the first confirmed match,
        // preferring ports whose USB vendor looks like a known serial bridge.
        var tasks = ports.ToDictionary(
            p => ProbePortAsync(p.PortName, baudRate, ct),
            p => p);

        var remaining = new List<Task<DetectionResult?>>(tasks.Keys);
        DetectionResult? firstUnlikely = null;

        while (remaining.Count > 0)
        {
            var any = await Task.WhenAny(remaining.Cast<Task>().Append(timeout)).ConfigureAwait(false);
            if (any == timeout)
                break; // leftover probes finish (and release their ports) on their own

            var finished = (Task<DetectionResult?>)any;
            remaining.Remove(finished);

            var result = await finished.ConfigureAwait(false);
            if (result == null)
                continue;

            if (tasks[finished].IsLikelyDevice)
                return result; // best possible match, stop early
            firstUnlikely ??= result;
        }

        return firstUnlikely;
    }

    private static async Task<DetectionResult?> ProbePortAsync(
        string port, int baudRate, CancellationToken ct)
    {
        if (!Probing.TryAdd(port, 0))
            return null;

        return await Task.Run(() =>
        {
            try
            {
                using var sp = new SerialPort(port, baudRate, Parity.None, 8, StopBits.One)
                {
                    NewLine = "\n",
                    ReadTimeout = 200,
                    WriteTimeout = 200,
                    DtrEnable = true,
                };
                sp.Open();

                // Boards with auto-reset reboot when the port opens, and the
                // bootloader eats (or is upset by) anything sent too early. The
                // firmware announces itself once it boots. Boards that don't reset
                // are asked directly, a few times in case a query gets lost.
                var sb = new StringBuilder();
                var start = DateTime.UtcNow;
                var deadline = start + TimeSpan.FromMilliseconds(4000);
                var nextAsk = start + TimeSpan.FromMilliseconds(1600);

                while (DateTime.UtcNow < deadline)
                {
                    if (ct.IsCancellationRequested)
                        return null;

                    if (DateTime.UtcNow >= nextAsk)
                    {
                        try { sp.Write("ID?\n"); } catch { /* ignore */ }
                        nextAsk = DateTime.UtcNow + TimeSpan.FromMilliseconds(800);
                    }

                    string chunk;
                    try { chunk = sp.ReadExisting(); }
                    catch (TimeoutException) { chunk = string.Empty; }

                    if (chunk.Length == 0)
                    {
                        Thread.Sleep(30);
                        continue;
                    }

                    sb.Append(chunk);
                    foreach (var line in sb.ToString().Split('\n'))
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || !t.Contains("\"Device\"", StringComparison.OrdinalIgnoreCase))
                            continue;
                        try
                        {
                            var identity = JsonSerializer.Deserialize<DeviceIdentity>(t, JsonOptions);
                            // Match on the constant signature so a renamed device
                            // is still recognized. It falls back to the name for older
                            // firmware that predates the Signature field.
                            if (identity?.Signature == "AudioMixer" || identity?.Device == "AudioMixer")
                                return new DetectionResult(port, identity);
                        }
                        catch (JsonException) { /* keep reading */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DeviceDetector] Probe {port} failed: {ex.Message}");
            }
            finally
            {
                Probing.TryRemove(port, out _);
            }
            return (DetectionResult?)null;
        }).ConfigureAwait(false);
    }

    /// <summary>Starts watching for USB device arrival/removal (Windows WMI).</summary>
    public void StartWatching()
    {
        if (_watcher != null)
            return;
        try
        {
            var query = new WqlEventQuery(
                "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3");
            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += (_, _) => DevicesChanged?.Invoke();
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceDetector] StartWatching failed: {ex.Message}");
            _watcher = null;
        }
    }

    public void Dispose()
    {
        try
        {
            _watcher?.Stop();
            _watcher?.Dispose();
        }
        catch
        {
            // ignore
        }
    }
}
