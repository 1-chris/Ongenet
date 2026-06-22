using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Vst.Vst2;
using Ongenet.Vst.Vst3;

namespace Ongenet.Vst;

/// <summary>
/// Discovers VST2 (<c>.dll</c>/<c>.so</c>/<c>.vst</c>) and VST3 (<c>.vst3</c>) plugins by scanning the
/// standard per-OS locations (plus the <c>VST_PATH</c> / <c>VST3_PATH</c> environment variables). Each
/// candidate is loaded just long enough to read its descriptor(s); bad or incompatible modules are
/// skipped. Pure filesystem/metadata work — safe to run off the UI thread.
/// </summary>
public sealed class VstPluginScanner
{
    /// <summary>How many modules to probe at once. Each read can spin up a Wine host (~5 s), so reading
    /// them one by one is painfully slow; this bounds the concurrency to keep memory/CPU sane.</summary>
    public const int MaxConcurrency = 10;

    private readonly Action<string>? _log;

    public VstPluginScanner(Action<string>? log = null) => _log = log;

    /// <summary>The directories searched for the given format (existing ones only), in order.</summary>
    public IReadOnlyList<string> SearchPaths(VstFormat format, IEnumerable<string>? extraPaths = null)
    {
        var paths = new List<string>();
        var envVar = format == VstFormat.Vst3 ? "VST3_PATH" : "VST_PATH";
        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(env))
            paths.AddRange(env.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        if (extraPaths is not null) paths.AddRange(extraPaths);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sub = format == VstFormat.Vst3 ? "VST3" : "VST";

        if (OperatingSystem.IsWindows())
        {
            var common = Environment.GetEnvironmentVariable("COMMONPROGRAMFILES");
            if (!string.IsNullOrEmpty(common)) paths.Add(Path.Combine(common, sub));
            var programFiles = Environment.GetEnvironmentVariable("PROGRAMFILES");
            if (!string.IsNullOrEmpty(programFiles))
            {
                paths.Add(Path.Combine(programFiles, "Common Files", sub));
                if (format == VstFormat.Vst2)
                {
                    paths.Add(Path.Combine(programFiles, "VSTPlugins"));
                    paths.Add(Path.Combine(programFiles, "Steinberg", "VSTPlugins"));
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            paths.Add($"/Library/Audio/Plug-Ins/{sub}");
            if (!string.IsNullOrEmpty(home)) paths.Add(Path.Combine(home, "Library", "Audio", "Plug-Ins", sub));
        }
        else // Linux
        {
            if (format == VstFormat.Vst3)
            {
                paths.Add("/usr/lib/vst3");
                paths.Add("/usr/local/lib/vst3");
                if (!string.IsNullOrEmpty(home)) paths.Add(Path.Combine(home, ".vst3"));
            }
            else
            {
                paths.Add("/usr/lib/vst");
                paths.Add("/usr/local/lib/vst");
                if (!string.IsNullOrEmpty(home)) paths.Add(Path.Combine(home, ".vst"));
            }
        }

        return paths
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Where(Directory.Exists)
            .ToList();
    }

    /// <summary>
    /// Every candidate module on disk (both formats), before any descriptor is read. Reading a module is
    /// the slow part — under yabridge each one spins up a Wine host — so callers process this list
    /// concurrently (see <see cref="ReadModule"/>).
    /// </summary>
    public IReadOnlyList<(VstFormat Format, string Path)> FindCandidates(IEnumerable<string>? extraPaths = null)
    {
        var list = new List<(VstFormat, string)>();
        foreach (var dir in SearchPaths(VstFormat.Vst2, extraPaths))
            foreach (var module in FindEntries(dir, Vst2Module.Extensions))
                list.Add((VstFormat.Vst2, module));
        foreach (var dir in SearchPaths(VstFormat.Vst3, extraPaths))
            foreach (var module in FindEntries(dir, new[] { ".vst3" }))
                list.Add((VstFormat.Vst3, module));
        return list;
    }

    /// <summary>Reads one candidate's descriptors (resolving format), skipping (logging) on error.</summary>
    public IReadOnlyList<VstPluginDescriptor> ReadModule(VstFormat format, string path)
        => format == VstFormat.Vst3 ? ReadVst3(path) : ReadVst2(path);

    /// <summary>Scans every search path of both formats and returns all plugin descriptors found.</summary>
    public IReadOnlyList<VstPluginDescriptor> Scan(IEnumerable<string>? extraPaths = null)
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<VstPluginDescriptor>();
        var options = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency };
        System.Threading.Tasks.Parallel.ForEach(FindCandidates(extraPaths), options, c =>
        {
            foreach (var d in ReadModule(c.Format, c.Path)) results.Add(d);
        });
        return results.ToList();
    }

    /// <summary>Reads the descriptors of a single VST2 module path, skipping (logging) on error.</summary>
    public IReadOnlyList<VstPluginDescriptor> ReadVst2(string path)
    {
        try { return Vst2Module.ReadDescriptors(path); }
        catch (Exception ex) { _log?.Invoke($"VST2: skipped '{path}': {ex.Message}"); return Array.Empty<VstPluginDescriptor>(); }
    }

    /// <summary>Reads the descriptors of a single VST3 bundle path, skipping (logging) on error.</summary>
    public IReadOnlyList<VstPluginDescriptor> ReadVst3(string path)
    {
        try { return Vst3Module.ReadDescriptors(path); }
        catch (Exception ex) { _log?.Invoke($"VST3: skipped '{path}': {ex.Message}"); return Array.Empty<VstPluginDescriptor>(); }
    }

    // Enumerates entries (files, or bundle dirs on macOS / for .vst3) whose name ends with one of the
    // given extensions, recursively under a directory.
    private static IEnumerable<string> FindEntries(string dir, IReadOnlyList<string> extensions)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories); }
        catch { yield break; }

        foreach (var e in entries)
            foreach (var ext in extensions)
                if (e.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { yield return e; break; }
    }
}
