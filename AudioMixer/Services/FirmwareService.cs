using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AudioMixer.Services;

/// <summary>
/// Flashes the bundled <c>firmware.hex</c> to an Arduino-bootloader ATmega328P
/// over the serial port using avrdude. avrdude is located from a bundled copy
/// next to the app, PlatformIO's packages, or the PATH.
/// </summary>
public sealed class FirmwareService
{
    private static string ExeDir => AppContext.BaseDirectory;

    /// <summary>Path to the firmware image shipped with the app, or null if missing.</summary>
    public string? FindHex()
    {
        foreach (var p in new[]
                 {
                     Path.Combine(ExeDir, "firmware.hex"),
                     Path.Combine(ExeDir, "firmware", "firmware.hex"),
                 })
        {
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    public bool IsAvailable => FindHex() != null;

    private static string FindAvrdude()
    {
        // 1) Bundled next to the exe (preferred for distribution).
        var bundled = Path.Combine(ExeDir, "avrdude", "avrdude.exe");
        if (File.Exists(bundled))
            return bundled;

        // 2) PlatformIO's copy, if installed.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var packages = Path.Combine(home, ".platformio", "packages");
        if (Directory.Exists(packages))
        {
            var fixedPath = Path.Combine(packages, "tool-avrdude", "avrdude.exe");
            if (File.Exists(fixedPath))
                return fixedPath;

            var match = Directory.GetDirectories(packages, "tool-avrdude*")
                .Select(d => Path.Combine(d, "avrdude.exe"))
                .FirstOrDefault(File.Exists);
            if (match != null)
                return match;
        }

        // 3) Fall back to the PATH.
        return "avrdude";
    }

    private static string? FindConf(string avrdudeExe)
    {
        var dir = Path.GetDirectoryName(avrdudeExe);
        if (dir == null)
            return null;
        foreach (var name in new[] { "avrdude.conf", Path.Combine("etc", "avrdude.conf") })
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    /// <summary>
    /// Flashes the firmware to <paramref name="port"/>. The caller must close any
    /// open serial connection first. Progress lines go to <paramref name="log"/>.
    /// </summary>
    public async Task<bool> FlashAsync(string port, Action<string> log, CancellationToken ct = default)
    {
        string? hex = FindHex();
        if (hex == null)
        {
            log("firmware.hex was not found next to the app.");
            return false;
        }

        string avrdude = FindAvrdude();
        string? conf = FindConf(avrdude);

        // Arduino bootloaders run at 115200 (Uno-style) or 57600 (older Nano). Try both.
        foreach (int baud in new[] { 115200, 57600 })
        {
            log($"Flashing {Path.GetFileName(hex)} on {port} at {baud} baud…");
            if (await RunAvrdude(avrdude, conf, port, baud, hex, log, ct).ConfigureAwait(false))
                return true;
            if (ct.IsCancellationRequested)
                return false;
            log($"No response at {baud} baud, trying another speed…");
        }

        log("Flashing failed. Check the cable/port and that the board has an Arduino bootloader.");
        return false;
    }

    private static async Task<bool> RunAvrdude(
        string avrdude, string? conf, string port, int baud, string hex,
        Action<string> log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = avrdude,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (conf != null)
        {
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(conf);
        }
        foreach (var a in new[] { "-c", "arduino", "-p", "atmega328p", "-P", port, "-b", baud.ToString(), "-U", $"flash:w:{hex}:i" })
            psi.ArgumentList.Add(a);

        try
        {
            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log(e.Data.Trim()); };
            proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log(e.Data.Trim()); };

            if (!proc.Start())
                return false;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            log($"avrdude could not be run: {ex.Message}");
            return false;
        }
    }
}
