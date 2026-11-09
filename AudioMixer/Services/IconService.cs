using System;
using System.Collections.Concurrent;
using System.IO;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using GdiIcon = System.Drawing.Icon;
using PngFormat = System.Drawing.Imaging.ImageFormat;

namespace AudioMixer.Services;

/// <summary>
/// Extracts an application's icon from its executable and converts it to an
/// Avalonia bitmap. Results are cached by path so repeated session refreshes are
/// cheap. Windows-only (uses System.Drawing).
/// </summary>
public sealed class IconService
{
    private readonly ConcurrentDictionary<string, AvaloniaBitmap?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public AvaloniaBitmap? GetIcon(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        return _cache.GetOrAdd(executablePath, Extract);
    }

    private static AvaloniaBitmap? Extract(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using GdiIcon? icon = GdiIcon.ExtractAssociatedIcon(path);
            if (icon == null)
                return null;

            using var gdiBitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            gdiBitmap.Save(stream, PngFormat.Png);
            stream.Position = 0;
            return new AvaloniaBitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
