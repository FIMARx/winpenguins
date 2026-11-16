using System.Text.Json;
using Microsoft.Win32;

namespace WinPenguins.Config;

public static class SettingsStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRunValueName = "WinPenguins";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPenguins");

    public static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    loaded.Normalize();
                    loaded.LaunchAtStartup = IsLaunchAtStartupEnabled();
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to defaults if config file is corrupted or unreadable.
        }

        var defaults = new AppSettings();
        defaults.LaunchAtStartup = IsLaunchAtStartupEnabled();
        return defaults;
    }

    public static void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(SettingsDirectory);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
        ApplyLaunchAtStartup(settings.LaunchAtStartup);
    }

    public static bool IsLaunchAtStartupEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(AppRunValueName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyLaunchAtStartup(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                return;
            }

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(AppRunValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppRunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Ignore registry permission issues gracefully.
        }
    }
}
