using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ongenet.Lv2;

/// <summary>
/// Discovers LV2 plugins by scanning the standard per-OS bundle locations (plus the <c>LV2_PATH</c>
/// environment variable). Each <c>*.lv2</c> bundle is read for its plugin metadata via
/// <see cref="Lv2Bundle"/>; bad bundles are skipped. Pure filesystem/metadata work — no plugin binary
/// is loaded, so it is safe to run off the UI thread.
/// </summary>
public sealed class Lv2PluginScanner
{
    private readonly Action<string>? _log;

    public Lv2PluginScanner(Action<string>? log = null) => _log = log;

    /// <summary>The directories that will be searched (existing ones only), in order.</summary>
    public IReadOnlyList<string> SearchPaths(IEnumerable<string>? extraPaths = null)
    {
        var paths = new List<string>();

        var lv2Path = Environment.GetEnvironmentVariable("LV2_PATH");
        if (!string.IsNullOrEmpty(lv2Path))
            paths.AddRange(lv2Path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        if (extraPaths is not null) paths.AddRange(extraPaths);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var common = Environment.GetEnvironmentVariable("COMMONPROGRAMFILES");
            if (!string.IsNullOrEmpty(common)) paths.Add(Path.Combine(common, "LV2"));
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData)) paths.Add(Path.Combine(appData, "LV2"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            paths.Add("/Library/Audio/Plug-Ins/LV2");
            if (!string.IsNullOrEmpty(home)) paths.Add(Path.Combine(home, "Library", "Audio", "Plug-Ins", "LV2"));
        }
        else
        {
            paths.Add("/usr/lib/lv2");
            paths.Add("/usr/local/lib/lv2");
            paths.Add("/usr/lib64/lv2");
            if (!string.IsNullOrEmpty(home)) paths.Add(Path.Combine(home, ".lv2"));
        }

        return paths
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Where(Directory.Exists)
            .ToList();
    }

    /// <summary>Scans the search paths and returns every plugin descriptor found.</summary>
    public IReadOnlyList<Lv2PluginDescriptor> Scan(IEnumerable<string>? extraPaths = null)
    {
        var results = new List<Lv2PluginDescriptor>();
        var seenBundles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dir in SearchPaths(extraPaths))
        foreach (var bundle in FindBundles(dir))
        {
            if (!seenBundles.Add(bundle)) continue;
            results.AddRange(ReadBundle(bundle));
        }

        return results;
    }

    /// <summary>Reads the plugin descriptors of a single bundle directory, skipping on error.</summary>
    public IReadOnlyList<Lv2PluginDescriptor> ReadBundle(string bundleDir)
    {
        try
        {
            return Lv2Bundle.Read(bundleDir);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"LV2: skipped '{bundleDir}': {ex.Message}");
            return Array.Empty<Lv2PluginDescriptor>();
        }
    }

    // Enumerates *.lv2 bundle directories under a search root (one level deep is typical, but some
    // distros nest; AllDirectories keeps it simple and only dirs containing a manifest are read).
    private static IEnumerable<string> FindBundles(string root)
    {
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root, "*.lv2", SearchOption.AllDirectories); }
        catch { yield break; }

        foreach (var d in dirs)
            if (File.Exists(Path.Combine(d, "manifest.ttl")))
                yield return d;
    }
}
