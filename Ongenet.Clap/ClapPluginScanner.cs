using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ongenet.Clap;

/// <summary>
/// Discovers CLAP plugins by scanning the standard per-OS locations (plus the <c>CLAP_PATH</c>
/// environment variable). Loads each module just long enough to read its descriptors; bad or
/// incompatible modules are skipped. Pure filesystem/metadata work — safe to run off the UI thread.
/// </summary>
public sealed class ClapPluginScanner
{
    private readonly Action<string>? _log;

    public ClapPluginScanner(Action<string>? log = null) => _log = log;

    /// <summary>The directories that will be searched (existing ones only), in order.</summary>
    public IReadOnlyList<string> SearchPaths(IEnumerable<string>? extraPaths = null)
    {
        var paths = new List<string>();

        var clapPath = Environment.GetEnvironmentVariable("CLAP_PATH");
        if (!string.IsNullOrEmpty(clapPath))
            paths.AddRange(clapPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        if (extraPaths is not null) paths.AddRange(extraPaths);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var common = Environment.GetEnvironmentVariable("COMMONPROGRAMFILES");
            if (!string.IsNullOrEmpty(common)) paths.Add(Path.Combine(common, "CLAP"));
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local)) paths.Add(Path.Combine(local, "Programs", "Common", "CLAP"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            paths.Add("/Library/Audio/Plug-Ins/CLAP");
            if (!string.IsNullOrEmpty(home)) paths.Add(Path.Combine(home, "Library", "Audio", "Plug-Ins", "CLAP"));
        }
        else
        {
            paths.Add("/usr/lib/clap");
            paths.Add("/usr/local/lib/clap");
            if (!string.IsNullOrEmpty(home)) paths.Add(Path.Combine(home, ".clap"));
        }

        return paths
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Where(Directory.Exists)
            .ToList();
    }

    /// <summary>Scans the search paths and returns every plugin descriptor found.</summary>
    public IReadOnlyList<ClapPluginDescriptor> Scan(IEnumerable<string>? extraPaths = null)
    {
        var results = new List<ClapPluginDescriptor>();
        foreach (var dir in SearchPaths(extraPaths))
        {
            foreach (var module in FindModules(dir))
            {
                results.AddRange(ReadModule(module));
            }
        }

        return results;
    }

    /// <summary>Reads the descriptors of a single module path (resolving macOS bundles), skipping on error.</summary>
    public IReadOnlyList<ClapPluginDescriptor> ReadModule(string modulePath)
    {
        var binary = ResolveBinary(modulePath);
        if (binary is null) return Array.Empty<ClapPluginDescriptor>();

        try
        {
            using var module = new ClapModule(binary);
            return module.ReadDescriptors();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"CLAP: skipped '{modulePath}': {ex.Message}");
            return Array.Empty<ClapPluginDescriptor>();
        }
    }

    // Enumerates *.clap entries (files on Win/Linux, bundle dirs on macOS) under a directory.
    private static IEnumerable<string> FindModules(string dir)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir, "*.clap", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var e in entries) yield return e;
    }

    // On macOS a .clap is a bundle directory; the loadable binary lives in Contents/MacOS/<name>.
    private static string? ResolveBinary(string path)
    {
        if (File.Exists(path)) return path;
        if (!Directory.Exists(path)) return null;

        var macOsDir = Path.Combine(path, "Contents", "MacOS");
        if (!Directory.Exists(macOsDir)) return null;
        return Directory.EnumerateFiles(macOsDir).FirstOrDefault();
    }
}
