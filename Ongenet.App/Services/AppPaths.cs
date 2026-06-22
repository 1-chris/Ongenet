using System;
using System.IO;

namespace Ongenet.App.Services;

/// <summary>
/// Resolves the per-user config directory using each OS's standard location:
/// Windows <c>%AppData%\Ongenet</c>, macOS <c>~/Library/Application Support/Ongenet</c>,
/// Linux <c>$XDG_CONFIG_HOME/Ongenet</c> (falling back to <c>~/.config/Ongenet</c>).
/// </summary>
public static class AppPaths
{
    private const string AppFolder = "Ongenet";

    public static string SettingsFile()
    {
        var dir = ConfigDirectory();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    /// <summary>User-saved presets directory (<c>&lt;config&gt;/Presets</c>); created on demand.</summary>
    public static string PresetsDirectory()
    {
        var dir = Path.Combine(ConfigDirectory(), "Presets");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Factory (built-in) presets directory (<c>&lt;config&gt;/Presets/Factory</c>), materialized once.</summary>
    public static string FactoryPresetsDirectory()
    {
        var dir = Path.Combine(PresetsDirectory(), "Factory");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string ConfigDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);

        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", AppFolder);

        // Linux / other: honour XDG_CONFIG_HOME, else ~/.config.
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(xdg))
            xdg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(xdg, AppFolder);
    }
}
