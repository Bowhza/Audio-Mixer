using System;
using System.Collections.ObjectModel;
using AudioMixer.Models;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AudioMixer.ViewModels;

/// <summary>
/// One physical fader: its channel index, what it controls, and its live level.
/// </summary>
public partial class FaderViewModel : ObservableObject
{
    public FaderViewModel(int channel, string assignment, ObservableCollection<AssignmentOption> options)
    {
        Channel = channel;
        _assignment = assignment ?? string.Empty;
        Options = options;
    }

    public int Channel { get; }

    public string Title => $"Fader {Channel + 1}";

    /// <summary>Shared picker options (wildcards + live sessions), owned by the main VM.</summary>
    public ObservableCollection<AssignmentOption> Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kind))]
    [NotifyPropertyChangedFor(nameof(KindGlyph))]
    [NotifyPropertyChangedFor(nameof(Description))]
    private string _assignment;

    /// <summary>The picked target (two-way bound to the fader's selector). Drives <see cref="Assignment"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AssignedIcon))]
    [NotifyPropertyChangedFor(nameof(HasAssignedIcon))]
    [NotifyPropertyChangedFor(nameof(AssignedLabel))]
    [NotifyPropertyChangedFor(nameof(AssignedGlyph))]
    private AssignmentOption? _selectedOption;

    partial void OnSelectedOptionChanged(AssignmentOption? value)
    {
        // User picked a target: persist its value.
        if (value != null &&
            !string.Equals(Assignment?.Trim(), value.Value, StringComparison.OrdinalIgnoreCase))
        {
            Assignment = value.Value;
        }
    }

    // ---- What the fader strip displays for its current assignment ------------

    /// <summary>The assigned app's icon (null for wildcards or when not running).</summary>
    public Bitmap? AssignedIcon => SelectedOption?.Icon;

    public bool HasAssignedIcon => SelectedOption?.Icon != null;

    /// <summary>Name shown on the strip: app name or wildcard label.</summary>
    public string AssignedLabel => SelectedOption?.DisplayName ?? Description;

    /// <summary>Glyph shown when there's no icon (wildcards / closed apps).</summary>
    public string AssignedGlyph => SelectedOption?.Glyph ?? KindGlyph;

    /// <summary>Live level 0-100 (calibrated) shown on the bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeLabel))]
    [NotifyPropertyChangedFor(nameof(FilledRow))]
    [NotifyPropertyChangedFor(nameof(UnfilledRow))]
    private double _volume;

    /// <summary>False until the device has reported this fader's position on the current connection.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeLabel))]
    private bool _hasReading;

    partial void OnVolumeChanged(double value) => HasReading = true;

    /// <summary>Forgets the last level (e.g. on disconnect) so a stale value isn't shown as live.</summary>
    public void ClearReading()
    {
        Volume = 0;
        HasReading = false;
    }

    public string VolumeLabel => HasReading ? $"{Volume:0}%" : "—";

    // ---- Mute -----------------------------------------------------------------

    /// <summary>
    /// When true the fader's target is held at 0% regardless of the slider. The
    /// bar keeps tracking the slider so unmuting restores the right level.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteGlyph))]
    [NotifyPropertyChangedFor(nameof(MuteLabel))]
    [NotifyPropertyChangedFor(nameof(BarOpacity))]
    private bool _isMuted;

    // Segoe MDL2 / Fluent icon glyphs: Mute (E74F) and Volume (E767).
    public string MuteGlyph => IsMuted ? "" : "";
    public string MuteLabel => IsMuted ? "Muted" : "Mute";

    /// <summary>The level bar is dimmed while muted.</summary>
    public double BarOpacity => IsMuted ? 0.35 : 1.0;

    /// <summary>Raised when the user toggles mute, so the main VM can apply it to audio.</summary>
    public event Action<FaderViewModel>? MuteToggled;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        MuteToggled?.Invoke(this);
    }

    // Proportional grid rows that reveal the bottom of a full-height gradient, so
    // the green→red colours stay fixed to absolute levels (no squishing).
    public GridLength FilledRow => new(Math.Clamp(Volume, 0, 100), GridUnitType.Star);
    public GridLength UnfilledRow => new(Math.Clamp(100 - Volume, 0, 100), GridUnitType.Star);

    // ---- Calibration --------------------------------------------------------

    /// <summary>Raw percent the slider reports at the bottom of its travel.</summary>
    [ObservableProperty] private double _calMin;

    /// <summary>Raw percent the slider reports at the top of its travel.</summary>
    [ObservableProperty] private double _calMax = 100;

    // Extremes observed during a calibration capture.
    private double _observedMin;
    private double _observedMax;

    /// <summary>Maps a raw reading through the calibration range onto 0-100%.</summary>
    public double Calibrate(double raw)
    {
        double span = CalMax - CalMin;
        if (span < 1)
            return Math.Clamp(raw, 0, 100); // not calibrated / degenerate
        double scaled = (raw - CalMin) / span * 100.0;
        return Math.Clamp(scaled, 0, 100);
    }

    /// <summary>True while a calibration capture is running (the strip shows its captured range).</summary>
    [ObservableProperty] private bool _isCalibrating;

    /// <summary>Captured range so far, e.g. "3–97%", shown in place of the mute button.</summary>
    [ObservableProperty] private string _calibrationRangeText = "Move slider";

    /// <summary>True once the captured travel is wide enough to be a real full sweep.</summary>
    [ObservableProperty] private bool _isCalibrationCaptured;

    /// <summary>Begins a calibration capture for this fader.</summary>
    public void BeginCalibration()
    {
        _observedMin = double.MaxValue;
        _observedMax = double.MinValue;
        CalibrationRangeText = "Move slider";
        IsCalibrationCaptured = false;
        IsCalibrating = true;
    }

    /// <summary>Feeds a raw reading into the current calibration capture.</summary>
    public void ObserveCalibration(double raw)
    {
        if (raw < _observedMin) _observedMin = raw;
        if (raw > _observedMax) _observedMax = raw;
        CalibrationRangeText = $"{_observedMin:0}–{_observedMax:0}%";
        IsCalibrationCaptured = _observedMax - _observedMin >= 50;
    }

    /// <summary>Commits the captured extremes (needs a usable span, else unchanged).</summary>
    public void CommitCalibration()
    {
        if (_observedMax - _observedMin >= 1)
        {
            CalMin = Math.Round(_observedMin, 1);
            CalMax = Math.Round(_observedMax, 1);
        }
        IsCalibrating = false;
    }

    /// <summary>Ends a capture without changing the calibration.</summary>
    public void CancelCalibration() => IsCalibrating = false;

    /// <summary>Short category label for the assignment badge.</summary>
    public string Kind => Assignment?.Trim() switch
    {
        "*" => "Master",
        "-" => "Unbound",
        "!" => "Active",
        null or "" => "Unassigned",
        _ => "App",
    };

    public string KindGlyph => Assignment?.Trim() switch
    {
        "*" => "🔊",
        "-" => "🗂",
        "!" => "🎯",
        null or "" => "➖",
        _ => "🎵",
    };

    public string Description => Assignment?.Trim() switch
    {
        "*" => "Master volume",
        "-" => "All unassigned apps",
        "!" => "Active / fullscreen window",
        null or "" => "Not assigned",
        _ => Assignment!.Trim(),
    };
}
