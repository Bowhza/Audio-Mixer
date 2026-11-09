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

    public string VolumeLabel => $"{Volume:0}%";

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

    /// <summary>Begins a calibration capture for this fader.</summary>
    public void BeginCalibration()
    {
        _observedMin = double.MaxValue;
        _observedMax = double.MinValue;
    }

    /// <summary>Feeds a raw reading into the current calibration capture.</summary>
    public void ObserveCalibration(double raw)
    {
        if (raw < _observedMin) _observedMin = raw;
        if (raw > _observedMax) _observedMax = raw;
    }

    /// <summary>Commits the captured extremes (needs a usable span, else unchanged).</summary>
    public void CommitCalibration()
    {
        if (_observedMax - _observedMin >= 1)
        {
            CalMin = Math.Round(_observedMin, 1);
            CalMax = Math.Round(_observedMax, 1);
        }
    }

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
