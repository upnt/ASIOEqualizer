using System;
using System.IO;
using System.Text.Json;

namespace VbEqualizer.Settings;

/// <summary>Reads/writes an AppSettings file at a caller-supplied path (see SettingsLocationStore
/// for where that path itself is remembered between launches).</summary>
public static class SettingsStore
{
    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VbEqualizer", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Never throws -- a missing, unreadable, or corrupt file just yields defaults.
    /// Use this for silent/best-effort loads (e.g. at startup); use TryLoad when the caller
    /// needs to tell the user a load actually failed.</summary>
    public static AppSettings Load(string path)
    {
        TryLoad(path, out var settings);
        return settings;
    }

    /// <summary>Like Load, but returns false (with settings set to defaults) instead of masking
    /// a missing/corrupt file, so an explicit user-triggered load can report the failure.</summary>
    public static bool TryLoad(string path, out AppSettings settings)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    settings = loaded;
                    return true;
                }
            }
        }
        catch { /* fall through to defaults */ }

        settings = new AppSettings();
        return false;
    }

    /// <summary>Throws on failure (e.g. an unwritable path) so the caller can tell the user.</summary>
    public static void Save(AppSettings settings, string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }
}
