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

    private const string VersionTag = "AMFW-VERSION:";
    private string? _bundledVersion;
    private bool _bundledVersionRead;

    /// <summary>
    /// Version of the bundled firmware.hex, read from the "AMFW-VERSION:x.y.z"
    /// tag the firmware embeds in its flash image. Null if the file is missing or
    /// predates the tag. Cached after the first read.
    /// </summary>
    public string? BundledVersion
    {
        get
        {
            if (!_bundledVersionRead)
            {
                _bundledVersion = ReadVersionFromHex(FindHex());
                _bundledVersionRead = true;
            }
            return _bundledVersion;
        }
    }

    private static string? ReadVersionFromHex(string? path)
    {
        if (path == null)
            return null;
        try
        {
            byte[] image = DecodeIntelHex(File.ReadAllLines(path));
            string text = System.Text.Encoding.ASCII.GetString(image);
            int i = text.IndexOf(VersionTag, StringComparison.Ordinal);
            if (i < 0)
                return null;
            i += VersionTag.Length;
            int end = i;
            while (end < text.Length && text[end] != '\0' && end - i < 32)
                end++;
            string version = text[i..end].Trim();
            return version.Length > 0 ? version : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FirmwareService] Reading firmware version failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Flattens an Intel HEX file's data records into a byte image.</summary>
    private static byte[] DecodeIntelHex(IEnumerable<string> lines)
    {
        var image = new SortedDictionary<long, byte>();
        long baseAddress = 0;

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length < 11 || line[0] != ':')
                continue;

            int count = Convert.ToInt32(line.Substring(1, 2), 16);
            int address = Convert.ToInt32(line.Substring(3, 4), 16);
            int type = Convert.ToInt32(line.Substring(7, 2), 16);
            if (line.Length < 11 + count * 2)
                continue;

            switch (type)
            {
                case 0x00: // data
                    for (int b = 0; b < count; b++)
                        image[baseAddress + address + b] = Convert.ToByte(line.Substring(9 + b * 2, 2), 16);
                    break;
                case 0x01: // end of file
                    goto done;
                case 0x02: // extended segment address
                    baseAddress = (long)Convert.ToInt32(line.Substring(9, 4), 16) << 4;
                    break;
                case 0x04: // extended linear address
                    baseAddress = (long)Convert.ToInt32(line.Substring(9, 4), 16) << 16;
                    break;
            }
        }

        done:
        if (image.Count == 0)
            return Array.Empty<byte>();

        long start = image.Keys.First();
        var bytes = new byte[image.Keys.Last() - start + 1];
        foreach (var (addr, value) in image)
            bytes[addr - start] = value;
        return bytes;
    }

    /// <summary>
    /// True when <paramref name="bundled"/> is newer than <paramref name="device"/>.
    /// A device that reports no (or an unreadable) version is treated as outdated.
    /// </summary>
    public static bool IsNewer(string? bundled, string? device)
    {
        if (!Version.TryParse(bundled, out var b))
            return false;
        if (!Version.TryParse(device, out var d))
            return true;
        return b > d;
    }

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
