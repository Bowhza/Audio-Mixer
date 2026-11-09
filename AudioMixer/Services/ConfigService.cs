using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AudioMixer.Models;

namespace AudioMixer.Services;

/// <summary>
/// Loads and saves <see cref="AppConfig"/> as <c>config.json</c> beside the
/// executable. Tolerant of the original hand-edited files (comments, trailing
/// commas, mixed-case keys).
/// </summary>
public static class ConfigService
{
    private static readonly string ConfigPath =
        Path.Combine(AppContext.BaseDirectory, "config.json");

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var config = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions);
                    if (config != null)
                    {
                        config.Applications ??= new();
                        return config;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigService] Load failed: {ex.Message}");
        }

        // First run (or unreadable file): create a sensible default.
        var fallback = new AppConfig();
        Save(fallback, out _);
        return fallback;
    }

    public static bool Save(AppConfig config, out string message)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, WriteOptions);
            File.WriteAllText(ConfigPath, json);
            message = "Configuration saved.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Error saving config: {ex.Message}";
            Debug.WriteLine($"[ConfigService] {message}");
            return false;
        }
    }
}
