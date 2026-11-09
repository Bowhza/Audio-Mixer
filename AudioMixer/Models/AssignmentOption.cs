using Avalonia.Media.Imaging;

namespace AudioMixer.Models;

/// <summary>
/// A selectable target in a fader's assignment dropdown: either a wildcard
/// (master / unbound / active window / unassigned) or a specific application.
/// <see cref="Value"/> is what gets persisted to <c>config.json</c>.
/// </summary>
public class AssignmentOption
{
    public required string Value { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Emoji shown for wildcards and apps that aren't currently running.</summary>
    public string? Glyph { get; init; }

    /// <summary>App icon for running applications (null for wildcards).</summary>
    public Bitmap? Icon { get; init; }

    public bool IsRunning { get; init; } = true;

    public bool HasIcon => Icon != null;
}
