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

    /// <summary>
    /// Bundled factory content pack (<c>Content/Core</c>) next to the app binary, or a repo-relative
    /// fallback during development. Returns <c>null</c> when no pack is present.
    /// </summary>
    public static string? FactoryContentDirectory()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Content", "Core");
        if (Directory.Exists(bundled)) return bundled;

        // Dev / test: walk up from the base directory looking for Content/Core.
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Content", "Core");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        catch { /* ignore */ }

        return null;
    }

    /// <summary>Factory samples folder inside the content pack, or null.</summary>
    public static string? FactorySamplesDirectory()
    {
        var root = FactoryContentDirectory();
        if (root is null) return null;
        var samples = Path.Combine(root, "Samples");
        return Directory.Exists(samples) ? samples : null;
    }

    /// <summary>Factory soundfonts folder inside the content pack, or null.</summary>
    public static string? FactorySoundFontsDirectory()
    {
        var root = FactoryContentDirectory();
        if (root is null) return null;
        var sf = Path.Combine(root, "Soundfonts");
        return Directory.Exists(sf) ? sf : null;
    }

    /// <summary>Factory control-surface definitions shipped with the app bundle.</summary>
    public static string FactoryControllersDirectory()
        => Path.Combine(AppContext.BaseDirectory, "Controllers", "Factory");

    /// <summary>User-imported or custom control-surface definitions.</summary>
    public static string UserControllersDirectory()
    {
        var dir = Path.Combine(ConfigDirectory(), "Controllers");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>User C# scripts directory (<c>Documents/Ongenet/Scripts</c>); created on demand.</summary>
    public static string UserScriptsDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            AppFolder,
            "Scripts");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Local crash dump directory (<c>&lt;config&gt;/crashes</c>). Written only on the device —
    /// never uploaded. Created on demand.
    /// </summary>
    public static string CrashesDirectory()
    {
        var dir = Path.Combine(ConfigDirectory(), "crashes");
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
