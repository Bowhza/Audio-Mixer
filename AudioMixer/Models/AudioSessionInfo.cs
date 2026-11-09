using Avalonia.Media.Imaging;

namespace AudioMixer.Models;

/// <summary>
/// A lightweight, UI-friendly description of a currently active audio session,
/// shown in the "Detected Sessions" list and the per-fader target picker.
/// </summary>
public class AudioSessionInfo
{
    public required string ProcessName { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string? ExecutablePath { get; init; }

    /// <summary>The app's icon, extracted from its executable (null if unavailable).</summary>
    public Bitmap? Icon { get; init; }

    public bool HasIcon => Icon != null;

    /// <summary>Label shown in lists: the friendly name, falling back to the process name.</summary>
    public string Label =>
        string.IsNullOrWhiteSpace(DisplayName) || DisplayName == ProcessName
            ? ProcessName
            : $"{DisplayName} ({ProcessName})";

    public override string ToString() => Label;
}
