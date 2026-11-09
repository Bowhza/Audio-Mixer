# Audio Mixer

The Audio Mixer is an open source project built around the **ATMega328p** with a lightweight driver for Windows. You can design and build your own audio mixer using potentiometers to seamlessly control the volume of your desktop applications.

## Introduction

This project should work on devices using the **ATMega328p MCU**, this includes the **Arduino Nano/Uno** boards and similar AVR devices like the ATMega168p.

### Hardware

The materials required:

- Compatible microcontroller.
- Potentiomenters (Fader recommended).
- Wire.
- Breadboard (Optional but recommended).

**Example Schematic**

The image shows how to wire up a fader/slider resistor to an Arduino UNO, though it would be the same for the NANO. Some sliders will clearly label the VCC, GND, and Output pins. When wiring up multiple sliders, use the subsequent analog channels such as A1, A2, A3...

<img src="./assets/Slider-Wiring.jpg" height="500px">

Alternatively, if you are using single turn potentiometers the pinout is provided below:

<img src="./assets/Single-Turn-Wiring.jpg" height="300px">

Here is an assembled breadboard prototype with 5 sliders all connected to the **Arduino NANO**, which is then connected to the computer using a USB-C cable.

<img src="./assets/Breadboard.jpg" height="400px">

### Software

**Basic Functionality**

- The microcontroller is programmed using bare metal C, and is transmitting JSON strings over the Serial Communication Interface.
- The desktop application provided acts as an audio driver, it will deserialize the JSON strings and adjust the volumes of assigned applications based on the values received.

**Desktop Application**

The driver is a modern **Avalonia (.NET 10)** app (`AudioMixer/`) with a dark
interface and a vertical fader console. It:

- **Auto-detects the mixer over USB**: scans serial ports, prefers known
  USB-serial bridges (CH340 / FTDI / Arduino / CP210x), confirms the device with
  an `ID?` handshake, and reconnects automatically on hot-plug.
- **Assigns apps by clicking a fader**: click a fader's target to pick from a
  list of the wildcards plus every app currently playing audio, each with its
  icon. The chosen app's icon and name show on the fader.
- **Shows live levels** on vertical bars with a fixed green → red gradient.
- **Calibrates each slider**: a guided mode maps a slider's real travel onto a
  clean 0-100% (handy when a pot doesn't swing the full range).
- **Adds / removes faders**, **renames the device**, and **tunes the jitter
  filter**: all stored on the device's EEPROM (see the Serial Protocol below),
  so they survive power cycles.
- **Flashes the firmware** from the settings menu, with `avrdude` and the
  firmware image bundled (no toolchain required).
- **Syncs on connect**: asks the device for every slider's current position so
  volumes and the UI match the hardware immediately.
- Builds its fader rows dynamically from the device, runs in the system tray,
  and keeps the same `config.json` format as before.

![Image of the audio driver application interface](/assets/Audio-Driver.png)

## Configuration File

The following is the configuration JSON file that is stored in the same directory as the audio driver application. It is created automatically on first run, and the user can edit it directly or use the GUI.

```json
{
  "BaudRate": 9600,
  "COMPort": "COM4",
  "AutoDetect": true,
  "Theme": "Dark",
  "Applications": ["*", "-", "chrome", "Discord", "Spotify"],
  "Calibration": [
    { "Min": 0, "Max": 100 }
  ]
}
```

- `AutoDetect`: when `true`, the driver finds and connects to the mixer on its
  own. When `false` it uses `COMPort`.
- `Calibration`: per-fader input range captured by the **Calibrate** button
  (maps each slider's real travel onto 0-100%). Optional.

### Wildcards

Wildcards are used to assign multiple applications to the same slider.

- `*` Master Volume Control.
- `-` Controls all applications that are not assigned to a slider.
- `!` Current Active Window/Fullscreen Window.

To assign a specific application to a slider, just pick it from that fader's
dropdown (it lists every app in the "Detected Sessions" list).

## Serial Protocol

The device streams newline-delimited JSON, one packet per fader change:

```json
{ "Channel": 0, "Voltage": 2.50, "Percentage": 50.0 }
```

It also responds to these commands from the driver:

- `ID?\n` → identify/handshake, used for USB auto-detection:

  ```json
  { "Device": "AudioMixer", "Signature": "AudioMixer", "Protocol": 1, "Faders": 5, "Hysteresis": 3, "Firmware": "2.0.0" }
  ```

  `Device` is the user-editable name, and `Signature` is a constant the driver
  matches on, so auto-detection keeps working even after a rename.

- `SYNC\n` → immediately resend the current value of every fader (sent by the
  driver on connect).

- `FADERS=n\n` → set the number of faders (1-8, one per ADC channel) and
  **persist it to the device's EEPROM**, then re-identify. Sent when you add or
  remove a fader with the **＋ Add** / **Remove fader** buttons, so the count
  survives power cycles.

- `NAME=x\n` → set the device's display name (up to 15 characters) and
  **persist it to EEPROM**, then re-identify. Sent by the **Rename** box in the
  settings menu.

- `HYST=n\n` → set the hysteresis / jitter-filter threshold in ADC counts
  (1-64) and **persist it to EEPROM**, then re-identify. Raise it if a slider's
  reading drifts while idle, or lower it for more responsiveness. Sent by the
  **Jitter filter** slider in the settings menu.

## Running the Application

The download for the audio driver can be found in the **[Releases](https://github.com/Bowhza/Audio-Mixer/releases/)** section. You can also build it yourself from the source in [`AudioMixer/`](https://github.com/Bowhza/Audio-Mixer/tree/main/AudioMixer) (the **.NET 10 SDK** is required):

```bash
cd AudioMixer
dotnet run -c Release
```

To produce a standalone executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

### Flashing the Code

> **Quickest option:** open the driver's **⚙ settings → Firmware → Flash
> firmware**. The app ships with `avrdude` and the compiled `firmware.hex` and
> flashes the connected board for you (no PlatformIO needed).

To build and flash the firmware yourself, the Platform-IO IDE [extension](https://marketplace.visualstudio.com/items?itemName=platformio.platformio-ide) is required for VSCode.

Once the extenstion is installed and your microcontroller is connected to your computer follow the steps below:

1. Click on the Platform-IO Icon on the sidebar. Followed by the "`Pick a folder`" button to open the project folder.

![Image of the sidebar](/assets/Platform-IO.png)

2. Navigate to where the project folder is downloaded, make sure that it is exctracted from the zip file.

![Image of the open folder file explorer](/assets/Open-Folder.png)

3. Select the folder and press `Select Folder` in the bottom right corner.

4. With the folder open, press `Ctrl + Shift + B` to open the command pallette and select `PlatformIO: Build`.

![Image of the command window open in VSCode](/assets/Build-Project.png)

5. If the project has built successfully, you will see a clear message in the terminal. You can continue to the next step.

6. Press `Ctrl + ALT + U` to upload the code to the microcontroller. Below is an image it the process completed successfully.

![Image of the upload success](/assets/Upload-Success.png)

7. The code is now flashed to the device, and you can run the provided executable.

### Changing the board

If you are running a microcontroller that does not use the ATMega328p, you can modify the configuration file for Platform-IO to use the board of your choice.

1. Open the Platform-IO extension and click on the `Projects & Configuration` option.

<img src="./assets/Platform-IO-Options.png" height="600px">

2. Click on the `Projects` button on the side bar, you should see a list of the projects.

![Image of the upload success](/assets/Platform-IO-Projects.png)

3. Click `Configure` for the Mixer project (The name may vary depending where the project is stored).

<img src="./assets/Platform-IO-Config.png" height="700px">

4. You should now see the configuration for the project where you can modify the `env` and `Board`.

5. After you are done modifying the configuration, click on `Save` in the top right corner.

**Notes**:

- You can directly modify the `platformio.ini` file if you prefer.
- As mentioned above, the code is designed to work on the ATmega328p and similar AVR microcontrollers.

## To-Do List

Things that can be improved, added or fixed:

- [x] Add functionality for the `!` wildcard.
- [x] Add auto config creation if one does not exist.
- [x] Improve code to account for hysteresis.
- [x] Auto-detect the device over USB (`ID?` handshake).
- [x] Report current slider states on connect (`SYNC`).
- [x] Calibrate slider ranges from the GUI.
- [x] Modernize the desktop app (Avalonia / .NET 10).
- [x] Configure the device over serial: fader count (`FADERS=`) and name
      (`NAME=`), persisted to the device's EEPROM.
- [x] Add / remove faders from the app.
- [x] Rename the device from the app.
- [x] Flash the firmware from inside the app.
- [x] Make the hysteresis threshold configurable over serial (`HYST=`, EEPROM).
- [ ] Add support for SSD1306 OLED to display various information on the mixer.
- [ ] Add support for linux.
- [ ] Other features...
