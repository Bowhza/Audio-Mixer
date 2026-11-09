using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AudioMixer.Models;

/// <summary>
/// Persisted application configuration (stored next to the executable as
/// <c>config.json</c>). Kept backward compatible with the original driver's
/// <c>BaudRate</c> / <c>COMPort</c> / <c>Applications</c> schema.
/// </summary>
public class AppConfig
{
    /// <summary>Serial baud rate. Must match the firmware (default 9600).</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>Preferred serial port, e.g. "COM3". Used when auto-detect is off.</summary>
    public string COMPort { get; set; } = "COM3";

    /// <summary>
    /// When true the driver scans USB serial ports, handshakes with the device
    /// and connects automatically, reconnecting on hot-plug.
    /// </summary>
    public bool AutoDetect { get; set; } = true;

    /// <summary>UI theme: "Dark" or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// Fader-to-target assignments, indexed by fader/channel number.
    /// Each entry is a process name, or a wildcard:
    /// <c>*</c> master, <c>-</c> unbound apps, <c>!</c> active/fullscreen window,
    /// empty string = unassigned.
    /// </summary>
    public List<string> Applications { get; set; } = new() { "*", "-", "!", "", "" };

    /// <summary>
    /// Per-fader input calibration, indexed by channel. Maps each slider's real
    /// travel (the raw percent it reports at the bottom and top of its throw)
    /// onto a clean 0-100%. Empty / absent means no calibration (identity).
    /// </summary>
    public List<SliderCalibration> Calibration { get; set; } = new();
}

/// <summary>
/// The raw reported percentage a slider produces at its physical extremes.
/// The driver remaps <c>[Min, Max]</c> onto <c>[0, 100]</c>.
/// </summary>
public class SliderCalibration
{
    public double Min { get; set; } = 0;
    public double Max { get; set; } = 100;
}
