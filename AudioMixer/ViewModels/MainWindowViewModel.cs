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

    // Settings writes (fader count, name, ...) awaiting confirmation from the
    // device's identify echo. Each is retried a few times, then reported as failed.
    private sealed class PendingWrite
    {
        public required Func<DeviceIdentity, bool> IsConfirmed { get; init; }
        public required Action Send { get; init; }
        public required string SuccessMessage { get; init; }
        public required string FailureMessage { get; init; }
        public int Attempts { get; set; }
    }

    private const string PendingFaders = "faders";
    private const string PendingName = "name";
    private const string PendingHysteresis = "hysteresis";
    private const int MaxWriteAttempts = 3;
    private readonly Dictionary<string, PendingWrite> _pendingWrites = new();
    private readonly DispatcherTimer _confirmTimer;

    // Set once the device has identified itself on the current connection.
    private bool _identityReceived;

    // Hot-plug: USB events arrive in bursts and before the COM port is ready, so
    // they are debounced. A scan requested while one is running is queued.
    private readonly DispatcherTimer _hotplugTimer;
    private bool _scanQueued;

    // The port the user picked in Manual mode (kept even while it's unplugged, so
    // the combo box falling back to another port never changes the user's choice).
    private string _preferredPort;
    private bool _refreshingPorts;

    public ObservableCollection<FaderViewModel> Faders { get; } = new();
    public ObservableCollection<AudioSessionInfo> Sessions { get; } = new();

    /// <summary>Targets offered by every fader's picker (wildcards + live sessions + assigned-but-closed apps).</summary>
    public ObservableCollection<AssignmentOption> AssignmentOptions { get; } = new();

    public ObservableCollection<SerialPortInfo> AvailablePorts { get; } = new();

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private string _deviceInfo = "No device";
    /// <summary>The name the device reports (shown in the header).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameDirty))]
    private string _deviceName = "AudioMixer";

    /// <summary>The name being typed in Settings. Separate so device replies never clobber typing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameDirty))]
    private string _editDeviceName = "AudioMixer";

    /// <summary>True when the Settings page (instead of the mixer) is shown.</summary>
    [ObservableProperty] private bool _isSettingsOpen;

    /// <summary>Jitter filter value on the slider (may not be applied yet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHysteresisDirty))]
    private int _hysteresis = 3;

    /// <summary>Jitter filter value the device actually uses.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHysteresisDirty))]
    [NotifyPropertyChangedFor(nameof(HysteresisDescription))]
    private int _deviceHysteresis = 3;

    /// <summary>Enables "Save name" only when the typed name differs from the device's.</summary>
    public bool IsNameDirty =>
        SanitizeDeviceName(EditDeviceName) is { Length: > 0 } n && !string.Equals(n, DeviceName, StringComparison.Ordinal);

    /// <summary>Enables "Apply" only when the slider differs from the device's value.</summary>
    public bool IsHysteresisDirty => Hysteresis != DeviceHysteresis;

    public string HysteresisDescription =>
        $"The mixer currently uses {DeviceHysteresis}. Higher keeps readings steady when you're not touching the sliders; lower reacts faster.";
    [ObservableProperty] private bool _autoDetect;
    [ObservableProperty] private SerialPortInfo? _selectedPort;
    [ObservableProperty] private string _baudRateText;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isCalibrating;
    [ObservableProperty] private bool _isFlashing;

    /// <summary>True when the bundled firmware is newer than what the connected device runs.</summary>
    [ObservableProperty] private bool _isFirmwareUpdateAvailable;

    /// <summary>e.g. "Update available: 2.0.1 → 2.0.2".</summary>
    [ObservableProperty] private string _firmwareUpdateText = "";

    /// <summary>Device vs bundled firmware versions, shown in Settings.</summary>
    [ObservableProperty] private string _firmwareVersionsText = "";

    public MainWindowViewModel()
    {
        _config = ConfigService.Load();
        _audio = new AudioService(_icons);
        _autoDetect = _config.AutoDetect;
        _baudRateText = _config.BaudRate.ToString();
        _preferredPort = _config.COMPort;

        BuildFaders(_config.Applications);
        Faders.CollectionChanged += OnFadersChanged;
        RebuildAssignmentOptions();
        UpdateFirmwareStatus(null, connected: false);

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
        _reconnectTimer.Tick += async (_, _) =>
        {
            DropVanishedPort();
            await EnsureConnectedAsync();
        };
        _reconnectTimer.Start();

        _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _confirmTimer.Tick += (_, _) => OnConfirmTimeout();

        _hotplugTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _hotplugTimer.Tick += async (_, _) =>
        {
            _hotplugTimer.Stop();
            RefreshPorts();
            DropVanishedPort();
            await EnsureConnectedAsync();
        };

        _detector.StartWatching();

        // Kick off the first connection attempt.
        _ = EnsureConnectedAsync();
    }

    // ---- Fader construction -------------------------------------------------

    private void BuildFaders(IReadOnlyList<string> assignments)
    {
        Faders.Clear();
        for (int i = 0; i < assignments.Count; i++)
            Faders.Add(CreateFader(i, assignments[i]));
    }

    /// <summary>Creates fader <paramref name="i"/> with its saved calibration, wired for mute.</summary>
    private FaderViewModel CreateFader(int i, string assignment)
    {
        var fader = new FaderViewModel(i, assignment, AssignmentOptions);
        if (i < _config.Calibration.Count)
        {
            fader.CalMin = _config.Calibration[i].Min;
            fader.CalMax = _config.Calibration[i].Max;
        }
        fader.MuteToggled += OnFaderMuteToggled;
        return fader;
    }

    /// <summary>
    /// Applies a mute toggle right away instead of waiting for the slider to move:
    /// 0% when muted, the fader's current level when unmuted.
    /// </summary>
    private void OnFaderMuteToggled(FaderViewModel fader)
    {
        StatusMessage = fader.IsMuted ? $"{fader.Title} muted." : $"{fader.Title} unmuted.";

        // Calibration doesn't drive audio, and with no reading there's no level to restore.
        if (IsCalibrating || (!fader.IsMuted && !fader.HasReading))
            return;

        float level = fader.IsMuted ? 0f : (float)fader.Volume;
        ApplyLevelInBackground(fader.Channel, level);
    }

    private void ApplyLevelInBackground(int channel, float level)
    {
        var assignments = Faders.Select(f => f.Assignment).ToList();
        _ = Task.Run(() => _audio.ApplyChannel(assignments, channel, level));
    }

    /// <summary>
    /// Resizes the fader list to <paramref name="count"/>. Faders that come back
    /// get their saved assignment and calibration from the config (not blanks),
    /// and removed ones are remembered in the config so re-adding restores them.
    /// </summary>
    private void ResizeFaders(int count)
    {
        if (count <= 0 || count == Faders.Count)
            return;

        // Remember the faders that are about to be removed.
        for (int i = count; i < Faders.Count; i++)
        {
            while (_config.Applications.Count <= i)
                _config.Applications.Add("");
            _config.Applications[i] = Faders[i].Assignment?.Trim() ?? "";
            while (_config.Calibration.Count <= i)
                _config.Calibration.Add(new SliderCalibration());
            _config.Calibration[i] = new SliderCalibration { Min = Faders[i].CalMin, Max = Faders[i].CalMax };
        }

        while (Faders.Count > count)
        {
            var removed = Faders[^1];
            // Don't leave a removed fader's app stuck at 0%: restore its level.
            if (removed.IsMuted && removed.HasReading && !IsCalibrating)
                ApplyLevelInBackground(removed.Channel, (float)removed.Volume);
            removed.MuteToggled -= OnFaderMuteToggled;
            Faders.RemoveAt(Faders.Count - 1);
        }

        while (Faders.Count < count)
        {
            int i = Faders.Count;
            string assignment = i < _config.Applications.Count ? _config.Applications[i] : "";
            Faders.Add(CreateFader(i, assignment));
        }

        RebuildAssignmentOptions();
    }

    /// <summary>Applies an identity reply. The device is the source of truth for the fader count.</summary>
    private void ApplyIdentity(DeviceIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.Device))
            DeviceName = identity.Device!;
        if (identity.Hysteresis > 0)
        {
            // Follow the device unless the user has an unapplied change on the slider.
            bool userEditing = IsHysteresisDirty && !_pendingWrites.ContainsKey(PendingHysteresis);
            DeviceHysteresis = identity.Hysteresis;
            if (!userEditing)
                Hysteresis = identity.Hysteresis;
        }

        DeviceInfo = $"Firmware {identity.Firmware} · {identity.Faders} faders";
        UpdateFirmwareStatus(identity.Firmware);

        // Keep the settings box in sync with the device unless the user is editing it.
        if (!IsSettingsOpen && !_pendingWrites.ContainsKey(PendingName) && !string.IsNullOrWhiteSpace(identity.Device))
            EditDeviceName = identity.Device!;

        foreach (var (key, write) in _pendingWrites.ToList())
        {
            if (!write.IsConfirmed(identity))
                continue;
            _pendingWrites.Remove(key);
            StatusMessage = write.SuccessMessage;
        }
        if (_pendingWrites.Count == 0)
            _confirmTimer.Stop();

        // While a fader-count change is unconfirmed, a reply with a different count
        // is stale (e.g. the answer to an earlier click), so keep the requested count.
        if (_pendingWrites.ContainsKey(PendingFaders))
            return;

        if (identity.Faders >= 1 && identity.Faders <= MaxFaders)
            ResizeFaders(identity.Faders);
    }

    /// <summary>
    /// Compares the device's firmware with the bundled firmware.hex. Pass null
    /// when no device is connected.
    /// </summary>
    private void UpdateFirmwareStatus(string? deviceVersion, bool connected = true)
    {
        string? bundled = _firmware.BundledVersion;
        string deviceText = connected ? (string.IsNullOrWhiteSpace(deviceVersion) ? "unknown" : deviceVersion!) : "not connected";
        string bundledText = bundled ?? "unknown";
        FirmwareVersionsText = $"Device: {deviceText} · Bundled: {bundledText}";

        bool wasAvailable = IsFirmwareUpdateAvailable;
        IsFirmwareUpdateAvailable = connected && FirmwareService.IsNewer(bundled, deviceVersion);
        FirmwareUpdateText = IsFirmwareUpdateAvailable
            ? $"Firmware update available: {deviceText} → {bundled}"
            : "";

        if (IsFirmwareUpdateAvailable && !wasAvailable)
            StatusMessage = $"{FirmwareUpdateText}. Click Update in the header to flash it.";
    }

    /// <summary>Sends a settings write and keeps retrying it until the device echoes it back.</summary>
    private void StartWrite(string key, PendingWrite write)
    {
        _pendingWrites[key] = write;
        write.Send();
        _confirmTimer.Stop();
        _confirmTimer.Start();
    }

    private void OnConfirmTimeout()
    {
        if (_pendingWrites.Count == 0)
        {
            _confirmTimer.Stop();
            return;
        }

        if (!_serial.IsOpen)
        {
            ClearPendingWrites();
            StatusMessage = "Lost the connection before the change was saved.";
            return;
        }

        bool gaveUp = false;
        foreach (var (key, write) in _pendingWrites.ToList())
        {
            if (++write.Attempts < MaxWriteAttempts)
            {
                write.Send();
                continue;
            }
            _pendingWrites.Remove(key);
            StatusMessage = write.FailureMessage;
            gaveUp = true;
        }

        if (_pendingWrites.Count == 0)
            _confirmTimer.Stop();

        // Resync the UI to whatever the device really holds.
        if (gaveUp)
            _serial.SendIdentifyQuery();
    }

    private void ClearPendingWrites()
    {
        _pendingWrites.Clear();
        _confirmTimer.Stop();
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
        // A muted fader holds its target at 0%, while the bar still follows the slider.
        var assignments = Faders.Select(f => f.Assignment).ToList();
        float level = Faders[channel].IsMuted ? 0f : (float)calibrated;
        _audio.ApplyChannel(assignments, channel, level);

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
            bool first = !_identityReceived;
            _identityReceived = true;
            ApplyIdentity(identity);

            // The ID?/SYNC sent when the port opened usually hit the bootloader
            // (opening the port resets the board) and were lost. The board is
            // clearly running now, so ask for every fader's position again.
            if (first)
                _serial.RequestSync();
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
                _identityReceived = false;
                _ = RetryIdentifyAsync();
            }
            else
            {
                ConnectionStatus = "Disconnected";
                DeviceInfo = "No device";
                UpdateFirmwareStatus(null, connected: false);
                ClearPendingWrites();
                foreach (var f in Faders)
                    f.ClearReading();
            }
        });
    }

    private void OnDevicesChanged()
    {
        // Fired on a WMI thread, often several times per plug and before the new
        // COM port can be opened. Restart the debounce so we act once it settles.
        Dispatcher.UIThread.Post(() =>
        {
            _hotplugTimer.Stop();
            _hotplugTimer.Start();
        });
    }

    /// <summary>
    /// A yanked USB cable doesn't always surface as a serial error, so the port
    /// can look open forever. Drop it when Windows no longer lists it.
    /// </summary>
    private void DropVanishedPort()
    {
        string? open = _serial.PortName;
        if (open == null)
            return;
        if (!System.IO.Ports.SerialPort.GetPortNames().Contains(open, StringComparer.OrdinalIgnoreCase))
        {
            _serial.Disconnect();
            StatusMessage = $"{DeviceName} was unplugged.";
        }
    }

    /// <summary>
    /// Opening the port usually resets the board, so the first ID?/SYNC can be
    /// swallowed by the bootloader. Ask again if the device stays quiet.
    /// </summary>
    private async Task RetryIdentifyAsync()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(2500);
            if (_identityReceived || !_serial.IsOpen)
                return;
            _serial.SendIdentifyQuery();
            _serial.RequestSync();
        }
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
        {
            // A scan is already running with a port list from before this
            // request (e.g. the mixer was just plugged in). Run again after it.
            _scanQueued = true;
            return;
        }

        _scanQueued = false;
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
                    ApplyIdentity(result.Identity);

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
                // Only ever open the port the user chose, and only once it exists.
                string port = _preferredPort;
                bool present = System.IO.Ports.SerialPort.GetPortNames()
                    .Contains(port, StringComparer.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(port))
                {
                    StatusMessage = "Pick a port in Settings.";
                }
                else if (!present)
                {
                    StatusMessage = $"Waiting for {port}. Plug the mixer in, or pick another port.";
                }
                else
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

        if (_scanQueued && !_serial.IsOpen)
            _ = Dispatcher.UIThread.InvokeAsync(EnsureConnectedAsync);
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

        // Rebuilding the list fires SelectedPort changes that are not user picks.
        _refreshingPorts = true;
        try
        {
            AvailablePorts.Clear();
            foreach (var p in ports)
                AvailablePorts.Add(p);

            // Prefer the user's chosen port (it may have just been plugged back in),
            // then whatever is connected, then anything.
            SelectedPort = AvailablePorts.FirstOrDefault(p =>
                               string.Equals(p.PortName, _preferredPort, StringComparison.OrdinalIgnoreCase))
                           ?? AvailablePorts.FirstOrDefault(p =>
                               string.Equals(p.PortName, _serial.PortName, StringComparison.OrdinalIgnoreCase))
                           ?? AvailablePorts.FirstOrDefault();
        }
        finally
        {
            _refreshingPorts = false;
        }
    }

    partial void OnSelectedPortChanged(SerialPortInfo? value)
    {
        if (_refreshingPorts || value == null)
            return;
        _preferredPort = value.PortName;
        SaveConnectionSettings();
        if (!AutoDetect && !_serial.IsOpen)
            _ = EnsureConnectedAsync();
    }

    // ---- Settings page ------------------------------------------------------

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void ShowMixer() => IsSettingsOpen = false;

    [RelayCommand]
    private void ShowSettings() => IsSettingsOpen = true;

    /// <summary>
    /// Connection settings take effect immediately, so they're saved immediately
    /// too. Only these fields are written (on top of the saved file), so unsaved
    /// fader assignments are not committed as a side effect.
    /// </summary>
    private void SaveConnectionSettings()
    {
        _config.AutoDetect = AutoDetect;
        _config.BaudRate = CurrentBaud;
        if (!string.IsNullOrWhiteSpace(_preferredPort))
            _config.COMPort = _preferredPort;

        var onDisk = ConfigService.Load();
        onDisk.AutoDetect = _config.AutoDetect;
        onDisk.BaudRate = _config.BaudRate;
        onDisk.COMPort = _config.COMPort;
        ConfigService.Save(onDisk, out _);
    }

    partial void OnIsSettingsOpenChanged(bool value)
    {
        // Start editing from the name the device actually holds.
        if (value && !_pendingWrites.ContainsKey(PendingName))
            EditDeviceName = DeviceName;
    }

    // ---- Commands -----------------------------------------------------------

    [RelayCommand]
    private void Rescan() => RefreshSessions();

    [RelayCommand]
    private async Task Reconnect()
    {
        SaveConnectionSettings(); // e.g. a changed baud rate
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
        if (!string.IsNullOrWhiteSpace(_preferredPort))
            _config.COMPort = _preferredPort;

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
        foreach (var f in Faders)
            f.CancelCalibration();
        IsCalibrating = false;
        StatusMessage = "Calibration cancelled.";
    }

    // ---- Fader count --------------------------------------------------------

    private const int MaxFaders = 8;

    // Add/Remove grey out at the hardware limits (8 = ADC0-ADC7, at least 1).
    private bool CanAddFader() => Faders.Count < MaxFaders;
    private bool CanRemoveFader() => Faders.Count > 1;

    public string AddFaderTip => CanAddFader() ? "Add a fader" : $"The mixer supports up to {MaxFaders} faders";

    private void OnFadersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        AddFaderCommand.NotifyCanExecuteChanged();
        RemoveFaderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(AddFaderTip));
    }

    [RelayCommand(CanExecute = nameof(CanAddFader))]
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

    [RelayCommand(CanExecute = nameof(CanRemoveFader))]
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
        if (!_serial.IsOpen)
        {
            StatusMessage = "Connect the mixer to change the jitter filter.";
            return;
        }

        StatusMessage = $"Saving jitter filter {h} to the mixer…";
        StartWrite(PendingHysteresis, new PendingWrite
        {
            IsConfirmed = id => id.Hysteresis == h,
            Send = () => _serial.SetHysteresis(h),
            SuccessMessage = $"Jitter filter set to {h}.",
            FailureMessage = $"The mixer did not confirm jitter filter {h}.",
        });
    }

    // ---- Device name --------------------------------------------------------

    [RelayCommand]
    private void RenameDevice()
    {
        // Apply the firmware's own rules up front (printable ASCII, no quotes or
        // backslashes, 15 chars), so the echo can be compared exactly.
        string name = SanitizeDeviceName(EditDeviceName);
        if (name.Length == 0)
        {
            StatusMessage = "Enter a device name (letters, digits, spaces, basic symbols).";
            return;
        }
        EditDeviceName = name;

        if (!_serial.IsOpen)
        {
            StatusMessage = "Connect the device to save its name.";
            return;
        }

        StatusMessage = $"Saving name \"{name}\" to the device…";
        StartWrite(PendingName, new PendingWrite
        {
            IsConfirmed = id => string.Equals(id.Device, name, StringComparison.Ordinal),
            Send = () => _serial.SetDeviceName(name),
            SuccessMessage = $"Renamed device to \"{name}\" (saved to the device).",
            FailureMessage = $"The device did not confirm the name \"{name}\".",
        });
    }

    private static string SanitizeDeviceName(string? input)
    {
        var chars = (input ?? "").Trim()
            .Where(c => c >= 0x20 && c <= 0x7E && c != '"' && c != '\\')
            .Take(15)
            .ToArray();
        return new string(chars).Trim();
    }

    /// <summary>
    /// Sends the new fader count to the device (if connected). It is only reported
    /// as saved once the device echoes it back, and is retried otherwise.
    /// </summary>
    private void PushFaderCount(int count, string action)
    {
        if (_serial.IsOpen)
        {
            StatusMessage = $"{action}. Saving {count} faders to the device…";
            StartWrite(PendingFaders, new PendingWrite
            {
                IsConfirmed = id => id.Faders == count,
                Send = () => _serial.SetFaderCount(count),
                SuccessMessage = $"{count} faders saved to the device.",
                FailureMessage = $"The device did not confirm {count} faders. Showing its stored count.",
            });
        }
        else
        {
            StatusMessage = $"{action}. Connect the device to store {count} faders in its memory.";
        }
    }

    partial void OnAutoDetectChanged(bool value)
    {
        StatusMessage = value ? "Finding the mixer automatically." : "Using the port you pick in Settings.";
        SaveConnectionSettings();
        if (value && !_serial.IsOpen)
            _ = EnsureConnectedAsync();
    }

    public void Dispose()
    {
        _sessionTimer.Stop();
        _reconnectTimer.Stop();
        _confirmTimer.Stop();
        _hotplugTimer.Stop();
        _serial.Dispose();
        _audio.Dispose();
        _detector.Dispose();
    }
}
