# AudioMixer (Avalonia driver)

A modernized rewrite of the Windows Audio Mixer driver, built on **Avalonia 11 /
.NET 10** with an MVVM architecture. It replaces the old WinForms / .NET Framework
4.8 `Audio-Driver` while keeping the same `config.json` format and serial
protocol, so existing hardware works unchanged (after flashing the updated
firmware for auto-detect).

## What's new

- **Modern UI**: a polished, themeable (dark/light) Fluent interface with live
  per-fader volume bars, assignment badges, and a tray icon (hides to tray on
  minimize/close, right-click to exit).
- **Smarter assignment**: each fader has a **dropdown** (no typing) listing the
  `*` / `-` / `!` wildcards and every app currently playing audio, each shown
  with its **icon**. The detected-sessions list also shows app icons and
  refreshes automatically. Assignments to a closed app are preserved (shown as
  "(not running)") so nothing is lost.
- **Better active-window handling**: the `!` wildcard follows the foreground
  process and correctly detects borderless/exclusive **fullscreen** windows
  (e.g. games) via the Win32 monitor APIs.
- **USB auto-detection**: on launch (and on USB hot-plug) the app scans serial
  ports, prioritizes known USB-serial bridges (CH340, FTDI, Arduino, CP210x,
  PL2303…), and confirms the real device with an `ID?` handshake before
  connecting. It auto-reconnects when the mixer is unplugged and replugged.
- **Dynamic fader count**: the UI builds itself from the device's handshake
  (`Faders` field) and from `config.json`, so it adapts to any number of faders.
- **Slider calibration**: a guided "Calibrate" mode captures each slider's real
  travel (e.g. a pot that only reads 18-100%) and remaps it onto a clean
  0-100%. Audio is paused while calibrating, and ranges are saved per channel in
  `config.json`.
- **State sync on connect**: on connect the driver sends `SYNC`, and the
  firmware reports every slider's current position immediately, so volumes and
  the UI match the physical sliders right away (no need to wiggle them first).

## Requirements

- Windows 10/11
- .NET 10 SDK (to build) or the .NET 10 Desktop Runtime (to run a framework-
  dependent build)

## Build & run

```bash
dotnet build -c Release
dotnet run -c Release
```

### Single-file publish

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The resulting `AudioMixer.exe` is self-contained, and `config.json` is created
next to it on first run.

## Protocol

The firmware streams newline-delimited JSON:

```json
{"Channel": 0, "Voltage": 2.50, "Percentage": 50.0}
```

and answers the handshake query `ID?\n` with:

```json
{"Device": "AudioMixer", "Protocol": 1, "Faders": 5, "Firmware": "2.0.0"}
```

See `../firmware/` for the matching firmware changes.

## Project layout

```
AudioMixer/
├─ Models/        AppConfig, MixerPacket/DeviceIdentity, AudioSessionInfo
├─ Services/      AudioService (NAudio/WASAPI), SerialService,
│                 DeviceDetector (USB + handshake + hot-plug),
│                 ConfigService, ActiveWindowService (Win32)
├─ ViewModels/    MainWindowViewModel, FaderViewModel
├─ Views/         MainWindow (XAML + code-behind)
├─ App.axaml      Theme resources, card/badge styles, tray icon
└─ Assets/        Mixer-Icon.ico
```
