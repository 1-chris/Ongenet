using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// Default <see cref="ISfzLoadService"/>. Resolves sample/include paths (case-insensitive fallback for
/// Windows-authored libraries) and loads each unique sample once, in parallel:
/// <list type="bullet">
/// <item>WAV samples are inspected by header only — short/looped/reversed ones are fully decoded
/// (resident), large forward one-shots keep just a RAM preload and stream from the original file (no full
/// decode, no float32 copy), which is what makes loading large libraries fast.</item>
/// <item>Other formats fall back to a full decode (resident, or a float32 raw cache when large).</item>
/// </list>
/// Decoding goes straight through the format decoders, skipping the waveform/tempo analysis that
/// <see cref="IAudioFileService"/> would do per sample.
/// </summary>
public sealed class SfzLoadService : ISfzLoadService
{
    private const double PreloadSeconds = 0.5;
    private const double ResidentSeconds = 6.0;

    private readonly IReadOnlyList<IAudioFileDecoder> _decoders;

    public SfzLoadService(IEnumerable<IAudioFileDecoder> decoders) => _decoders = decoders.ToList();

    public SfzLoadResult? Load(string sfzPath, IProgress<double>? progress = null)
    {
        string text;
        try { text = File.ReadAllText(sfzPath); }
        catch { return null; }
        return Build(text, Path.GetFullPath(sfzPath), progress);
    }

    public SfzLoadResult LoadFromText(string sfzText, string sfzPath, IProgress<double>? progress = null)
        => Build(sfzText, string.IsNullOrEmpty(sfzPath) ? Path.GetFullPath("instrument.sfz") : Path.GetFullPath(sfzPath), progress);

    private SfzLoadResult Build(string text, string absSfzPath, IProgress<double>? progress)
    {
        var baseDir = Path.GetDirectoryName(absSfzPath) ?? Directory.GetCurrentDirectory();

        var options = new SfzParseOptions
        {
            IncludeResolver = inc =>
            {
                var resolved = ResolveFile(baseDir, string.Empty, inc);
                if (resolved is null) return null;
                try { return File.ReadAllText(resolved); }
                catch { return null; }
            }
        };

        var document = SfzParser.Parse(text, options);
        var defaultPath = document.Control.DefaultPath;
        var forceResident = CollectRandomAccessSamples(document);

        var warnings = new List<string>(document.Warnings);
        var missing = new HashSet<string>(StringComparer.Ordinal);

        // Resolve each region's sample to a file once; de-duplicate by resolved path.
        var opcodeToResolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var resolvedForceResident = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var region in document.Regions)
        {
            var op = region.Sample;
            if (op.Length == 0 || opcodeToResolved.ContainsKey(op)) continue;
            var resolved = ResolveFile(baseDir, defaultPath, op);
            if (resolved is null) { missing.Add(op); continue; }
            opcodeToResolved[op] = resolved;
            var force = forceResident.Contains(op);
            resolvedForceResident[resolved] = resolvedForceResident.TryGetValue(resolved, out var prev) ? prev || force : force;
        }

        // Load unique samples in parallel (independent file I/O + decode).
        var loaded = new ConcurrentDictionary<string, SfzSample>(StringComparer.OrdinalIgnoreCase);
        var failed = new ConcurrentBag<string>();
        var entries = resolvedForceResident.Keys.ToArray();
        var done = 0;

        Parallel.ForEach(entries,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) },
            resolved =>
            {
                try
                {
                    var sample = LoadOne(resolved, resolvedForceResident[resolved]);
                    if (sample is not null) loaded[resolved] = sample;
                    else failed.Add(resolved);
                }
                catch (Exception ex) { failed.Add($"{resolved}: {ex.Message}"); }

                var n = System.Threading.Interlocked.Increment(ref done);
                progress?.Report(entries.Length == 0 ? 1.0 : (double)n / entries.Length);
            });

        foreach (var f in failed) warnings.Add($"Failed to load '{f}'");

        // Map every opcode to its loaded sample.
        var byOpcode = new Dictionary<string, SfzSample>(StringComparer.Ordinal);
        foreach (var (op, resolved) in opcodeToResolved)
        {
            if (loaded.TryGetValue(resolved, out var s)) byOpcode[op] = s;
            else missing.Add(op);
        }

        progress?.Report(1.0);

        return new SfzLoadResult
        {
            Document = document,
            Library = new SfzSampleLibrary(byOpcode),
            SfzPath = absSfzPath,
            SfzText = text,
            DisplayName = Path.GetFileNameWithoutExtension(absSfzPath),
            MissingSamples = missing.ToList(),
            Warnings = warnings
        };
    }

    // Loads one resolved sample file into the right tier.
    private SfzSample? LoadOne(string resolved, bool forceResident)
    {
        // Fast path: WAV read by header. Large forward one-shots stream from the original file.
        var layout = WavLayout.Read(resolved);
        if (layout is not null && layout.FrameCount > 0)
        {
            var residentLimit = (long)(ResidentSeconds * layout.SampleRate);
            if (forceResident || layout.FrameCount <= residentLimit)
            {
                using var fs = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
                return SfzSample.FromResident(WavParser.Parse(fs));
            }

            var preloadFrames = Math.Min(layout.FrameCount, (long)(PreloadSeconds * layout.SampleRate));
            var preload = layout.ReadFrames(resolved, 0, preloadFrames);
            return SfzSample.FromStream(resolved, layout.DataOffset, layout.Channels, layout.SampleRate,
                layout.BitsPerSample, layout.IsFloat, layout.FrameCount, preload, preloadFrames);
        }

        // Fallback: non-WAV formats decode fully (no waveform/tempo pass).
        var decoder = _decoders.FirstOrDefault(d => d.CanDecode(resolved));
        if (decoder is null) return null;
        var buf = decoder.Decode(resolved);

        var residentLimit2 = (long)(ResidentSeconds * buf.SampleRate);
        if (forceResident || buf.FrameCount <= residentLimit2)
            return SfzSample.FromResident(buf);

        // Large compressed sample: cache as a float32 raw file and stream from it.
        var preloadFrames2 = Math.Min(buf.FrameCount, (long)(PreloadSeconds * buf.SampleRate));
        var preload2 = new float[preloadFrames2 * buf.Channels];
        Array.Copy(buf.Samples, preload2, preload2.Length);

        var path = Path.Combine(Path.GetTempPath(), $"ongen-sfz-{Guid.NewGuid():N}.raw");
        using (var fs = File.Create(path, 1 << 20)) fs.Write(MemoryMarshal.AsBytes(buf.Samples.AsSpan()));
        SfzTempFiles.Track(path);

        return SfzSample.FromStream(path, 0, buf.Channels, buf.SampleRate, 32, isFloat: true,
            buf.FrameCount, preload2, preloadFrames2);
    }

    private static HashSet<string> CollectRandomAccessSamples(SfzDocument document)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in document.Regions)
        {
            var loop = region.Opcodes.Get("loop_mode") ?? region.Opcodes.Get("loopmode");
            var reverse = (region.Opcodes.Get("direction") ?? "forward") == "reverse";
            if (reverse || (loop is not null && loop != "no_loop")) set.Add(region.Sample);
        }

        return set;
    }

    // Resolves baseDir + defaultPath + relative into an existing file path, trying an exact match first
    // then a case-insensitive walk of each path segment (for Windows-authored libraries on Linux).
    private static string? ResolveFile(string baseDir, string defaultPath, string relative)
    {
        var combined = (defaultPath + relative).Replace('\\', '/');
        var exact = Path.GetFullPath(Path.Combine(baseDir, combined));
        if (File.Exists(exact)) return exact;
        return ResolveCaseInsensitive(baseDir, combined);
    }

    private static string? ResolveCaseInsensitive(string baseDir, string relative)
    {
        var current = baseDir;
        if (!Directory.Exists(current)) return null;

        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment == ".") continue;
            if (segment == "..") { current = Path.GetDirectoryName(current) ?? current; continue; }

            var isLast = i == segments.Length - 1;
            string? match = null;
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    if (string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase))
                    {
                        match = entry;
                        break;
                    }
                }
            }
            catch { return null; }

            if (match is null) return null;
            if (isLast) return File.Exists(match) ? match : null;
            current = match;
        }

        return null;
    }
}
