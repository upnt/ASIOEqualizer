using System;
using System.IO;
using System.Text.Json;

namespace VbEqualizer.Settings;

/// <summary>
/// Remembers, at a fixed well-known path, which settings.json the user has pointed the app at
/// (see SettingsStore for the actual settings file, which can live anywhere). This is the one
/// piece of state that must live at a fixed location, since it's what tells the app where to
/// look at startup.
/// </summary>
public static class SettingsLocationStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VbEqualizer", "location.json");

    private sealed class Location
    {
        public string? SettingsFilePath { get; set; }
    }

    /// <summary>Returns null (meaning "use SettingsStore.DefaultPath") if nothing was ever chosen.</summary>
    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Location>(json)?.SettingsFilePath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Best-effort -- this is just a convenience pointer, not the settings themselves.</summary>
    public static void Save(string settingsFilePath)
    {
        try
        {
            string? folder = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            string json = JsonSerializer.Serialize(new Location { SettingsFilePath = settingsFilePath });
            File.WriteAllText(FilePath, json);
        }
        catch { /* best effort */ }
    }
}
