using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sfz;

/// <summary>
/// Loads an SFZ patch into format-neutral <see cref="SamplerRegion"/>s. Resolves sample/include paths
/// (case-insensitive fallback for Windows-authored libraries) and loads each unique sample once, in
/// parallel:
/// <list type="bullet">
/// <item>WAV samples are inspected by header only — short/looped/reversed ones are fully decoded
/// (resident), large forward one-shots keep just a RAM preload and stream from the original file (no full
/// decode, no float32 copy), which is what makes loading large libraries fast.</item>
/// <item>Other formats fall back to a full decode (resident, or a float32 raw cache when large).</item>
/// </list>
/// Decoding goes straight through the format decoders, skipping the waveform/tempo analysis that
/// <c>IAudioFileService</c> would do per sample.
/// </summary>
public sealed class SfzLoader
{
    private const double PreloadSeconds = 0.5;
    private const double ResidentSeconds = 6.0;

    private readonly IReadOnlyList<IAudioFileDecoder> _decoders;

    public SfzLoader(IReadOnlyList<IAudioFileDecoder> decoders) => _decoders = decoders;

    public SamplerLoadResult? Load(string sfzPath, IProgress<double>? progress)
    {
        string text;
        try { text = File.ReadAllText(sfzPath); }
        catch { return null; }
        return Build(text, Path.GetFullPath(sfzPath), progress);
    }

    public SamplerLoadResult LoadFromText(string sfzText, string sfzPath, IProgress<double>? progress)
        => Build(sfzText, string.IsNullOrEmpty(sfzPath) ? Path.GetFullPath("instrument.sfz") : Path.GetFullPath(sfzPath), progress);

    private SamplerLoadResult Build(string text, string absSfzPath, IProgress<double>? progress)
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
        var loaded = new ConcurrentDictionary<string, SamplerSample>(StringComparer.OrdinalIgnoreCase);
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
        var byOpcode = new Dictionary<string, SamplerSample>(StringComparer.Ordinal);
        foreach (var (op, resolved) in opcodeToResolved)
        {
            if (loaded.TryGetValue(resolved, out var s)) byOpcode[op] = s;
            else missing.Add(op);
        }

        var library = new SamplerSampleLibrary(byOpcode);
        var regions = BuildRegions(document, library);

        progress?.Report(1.0);

        return new SamplerLoadResult
        {
            Regions = regions,
            Library = library,
            Path = absSfzPath,
            DisplayName = Path.GetFileNameWithoutExtension(absSfzPath),
            Format = SamplerFormat.Sfz,
            SourceText = text,
            PresetIndex = -1,
            MissingSamples = missing.ToList(),
            Warnings = warnings
        };
    }

    /// <summary>Builds playable regions from a parsed SFZ document + its decoded samples. Regions whose
    /// sample failed to load are skipped. Exposed for reuse + testing.</summary>
    public static IReadOnlyList<SamplerRegion> BuildRegions(SfzDocument document, SamplerSampleLibrary library)
    {
        var list = new List<SamplerRegion>(document.Regions.Count);
        foreach (var region in document.Regions)
        {
            var rt = BuildRegion(region, library.Get(region.Sample));
            if (rt is not null) list.Add(rt);
        }

        return list;
    }

    // Loads one resolved sample file into the right tier.
    private SamplerSample? LoadOne(string resolved, bool forceResident)
    {
        // Fast path: WAV read by header. Large forward one-shots stream from the original file.
        var layout = WavLayout.Read(resolved);
        if (layout is not null && layout.FrameCount > 0)
        {
            var residentLimit = (long)(ResidentSeconds * layout.SampleRate);
            if (forceResident || layout.FrameCount <= residentLimit)
            {
                using var fs = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
                return SamplerSample.FromResident(WavParser.Parse(fs));
            }

            var preloadFrames = Math.Min(layout.FrameCount, (long)(PreloadSeconds * layout.SampleRate));
            var preload = layout.ReadFrames(resolved, 0, preloadFrames);
            return SamplerSample.FromStream(resolved, layout.DataOffset, layout.Channels, layout.SampleRate,
                layout.BitsPerSample, layout.IsFloat, layout.FrameCount, preload, preloadFrames);
        }

        // Fallback: non-WAV formats decode fully (no waveform/tempo pass).
        var decoder = _decoders.FirstOrDefault(d => d.CanDecode(resolved));
        if (decoder is null) return null;
        var buf = decoder.Decode(resolved);

        var residentLimit2 = (long)(ResidentSeconds * buf.SampleRate);
        if (forceResident || buf.FrameCount <= residentLimit2)
            return SamplerSample.FromResident(buf);

        // Large compressed sample: cache as a float32 raw file and stream from it.
        var preloadFrames2 = Math.Min(buf.FrameCount, (long)(PreloadSeconds * buf.SampleRate));
        var preload2 = new float[preloadFrames2 * buf.Channels];
        Array.Copy(buf.Samples, preload2, preload2.Length);

        var path = Path.Combine(Path.GetTempPath(), $"ongen-sfz-{Guid.NewGuid():N}.raw");
        using (var fs = File.Create(path, 1 << 20)) fs.Write(MemoryMarshal.AsBytes(buf.Samples.AsSpan()));
        SamplerTempFiles.Track(path);

        return SamplerSample.FromStream(path, 0, buf.Channels, buf.SampleRate, 32, isFloat: true,
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

    // --- Region building (formerly SfzRegionRuntime.Build) ---

    /// <summary>Builds a runtime region from a parsed SFZ region and its decoded sample (null if missing).</summary>
    private static SamplerRegion? BuildRegion(SfzRegion region, SamplerSample? sample)
    {
        if (sample is null) return null;
        var o = region.Opcodes;

        var key = SfzNote.Parse(o.Get("key"));
        var loKey = o.GetKey("lokey", key ?? 0);
        var hiKey = o.GetKey("hikey", key ?? 127);
        var keycenter = o.GetKey("pitch_keycenter", key ?? 60);

        var volume = o.GetDouble("volume", 0.0);
        var amplitude = o.GetDouble("amplitude", 100.0);

        var loopMode = ParseLoopMode(o.Get("loop_mode") ?? o.Get("loopmode"));
        var frames = sample.FrameCount;
        var offset = Clamp(o.GetInt("offset", 0), 0, frames);
        var end = o.GetInt("end", -1);
        var endFrame = end < 0 ? frames : Clamp(end + 1, 0, frames); // SFZ end is the last frame index
        var loopStart = Clamp((long)o.GetInt("loop_start", o.GetInt("loopstart", 0)), 0, frames);
        var loopEndOp = o.GetInt("loop_end", o.GetInt("loopend", -1));
        var loopEnd = loopEndOp < 0 ? endFrame : Clamp(loopEndOp + 1, 0, frames);

        // Filter.
        var cutoff = o.GetDouble("cutoff", -1);
        var hasFilter = cutoff > 0;
        var resonance = o.GetDouble("resonance", 0.0);
        var filterQ = 0.70710678 * AudioMath.Db2Lin(resonance);

        // Filter modulation.
        var filEgDepth = o.GetDouble("fileg_depth", 0.0);
        var hasFilEg = hasFilter && filEgDepth != 0.0;
        var filLfoFreq = o.GetDouble("fillfo_freq", 0.0);
        var filLfoDepth = o.GetDouble("fillfo_depth", 0.0);
        var hasFilLfo = hasFilter && filLfoFreq > 0.0 && filLfoDepth != 0.0;

        // Amp LFO.
        var ampLfoFreq = o.GetDouble("amplfo_freq", 0.0);
        var ampLfoDepth = o.GetDouble("amplfo_depth", 0.0);
        var hasAmpLfo = ampLfoFreq > 0.0 && ampLfoDepth != 0.0;

        // Pitch LFO + EG.
        var pitchLfoFreq = o.GetDouble("pitchlfo_freq", 0.0);
        var pitchLfoDepth = o.GetDouble("pitchlfo_depth", 0.0);
        var hasPitchLfo = pitchLfoFreq > 0.0 && pitchLfoDepth != 0.0;
        var pitchEgDepth = o.GetDouble("pitcheg_depth", 0.0);
        var hasPitchEg = pitchEgDepth != 0.0;

        // EQ bands.
        var eqBands = new List<SamplerEqBand>(3);
        for (var i = 1; i <= 3; i++)
        {
            var freq = o.GetDouble($"eq{i}_freq", -1);
            var gain = o.GetDouble($"eq{i}_gain", 0.0);
            if (freq > 0 && gain != 0.0)
                eqBands.Add(new SamplerEqBand(freq, gain, o.GetDouble($"eq{i}_bw", 1.0)));
        }

        // CC → cutoff (cutoff_ccN / cutoff_onccN); each adds `value` cents at CC 127.
        var cutoffCc = new List<SamplerCcMod>();
        foreach (var kv in o.Raw)
        {
            var cc = ParseCcOpcode(kv.Key, "cutoff_cc") ?? ParseCcOpcode(kv.Key, "cutoff_oncc");
            if (cc is { } n && double.TryParse(kv.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var cents) && cents != 0.0)
            {
                cutoffCc.Add(new SamplerCcMod(n, cents));
            }
        }

        var bendDown = o.GetDouble("bend_down", -200.0);
        var swLast = o.GetKey("sw_last", o.GetKey("sw_down", -1));

        return new SamplerRegion
        {
            Sample = sample,
            LoKey = loKey,
            HiKey = hiKey,
            LoVel = o.GetInt("lovel", 0),
            HiVel = o.GetInt("hivel", 127),
            PitchKeycenter = keycenter,
            KeytrackSemisPerKey = o.GetDouble("pitch_keytrack", 100.0) / 100.0,
            TransposeSemis = o.GetDouble("transpose", 0.0),
            TuneCents = o.GetDouble("tune", o.GetDouble("pitch", 0.0)),
            Gain = AudioMath.Db2Lin(volume) * (amplitude / 100.0),
            Pan = AudioMath.Clamp(o.GetDouble("pan", 0.0) / 100.0, -1.0, 1.0),
            AmpVeltrack = o.GetDouble("amp_veltrack", 100.0),
            AmpEg = ReadEg(o, "ampeg", 100.0),
            Offset = offset,
            End = endFrame,
            LoopMode = loopMode,
            LoopStart = loopStart,
            LoopEnd = loopEnd > loopStart ? loopEnd : endFrame,
            Reverse = (o.Get("direction") ?? "forward") == "reverse",
            SeqLength = o.GetInt("seq_length", 1) < 1 ? 1 : o.GetInt("seq_length", 1),
            SeqPosition = o.GetInt("seq_position", 1),
            RoundRobinKey = region.GroupIndex >= 0
                ? region.GroupIndex
                : unchecked(loKey * 1000003 + hiKey * 1009 + o.GetInt("lovel", 0) * 31 + o.GetInt("hivel", 127)),
            Group = o.GetInt("group", 0),
            OffBy = o.GetInt("off_by", -1),

            HasFilter = hasFilter,
            FilterMode = MapFilterType(o.Get("fil_type")),
            Cutoff = cutoff,
            FilterQ = filterQ,
            FilKeytrack = o.GetDouble("fil_keytrack", 0.0),
            FilKeycenter = o.GetKey("fil_keycenter", 60),
            FilVeltrack = o.GetDouble("fil_veltrack", 0.0),
            HasFilEg = hasFilEg,
            FilEgDepth = filEgDepth,
            FilEg = ReadEg(o, "fileg", 100.0),
            HasFilLfo = hasFilLfo,
            FilLfoFreq = filLfoFreq,
            FilLfoDepth = filLfoDepth,
            FilLfoDelay = o.GetDouble("fillfo_delay", 0.0),
            HasAmpLfo = hasAmpLfo,
            AmpLfoFreq = ampLfoFreq,
            AmpLfoDepthDb = ampLfoDepth,
            AmpLfoDelay = o.GetDouble("amplfo_delay", 0.0),
            HasPitchLfo = hasPitchLfo,
            PitchLfoFreq = pitchLfoFreq,
            PitchLfoDepth = pitchLfoDepth,
            PitchLfoDelay = o.GetDouble("pitchlfo_delay", 0.0),
            HasPitchEg = hasPitchEg,
            PitchEgDepth = pitchEgDepth,
            PitchEg = ReadEg(o, "pitcheg", 0.0),
            EqBands = eqBands,

            Trigger = ParseTrigger(o.Get("trigger")),
            SwLast = swLast,
            SwLoKey = o.GetKey("sw_lokey", -1),
            SwHiKey = o.GetKey("sw_hikey", -1),
            SwDefault = o.GetKey("sw_default", -1),
            BendUpCents = o.GetDouble("bend_up", 200.0),
            BendDownCents = Math.Abs(bendDown),
            CutoffCc = cutoffCc
        };
    }

    // Reads an {prefix}_delay/attack/hold/decay/sustain/release envelope from opcodes.
    private static SamplerEgSpec ReadEg(SfzOpcodes o, string prefix, double defaultSustain) => new()
    {
        Delay = o.GetDouble(prefix + "_delay", 0.0),
        Attack = o.GetDouble(prefix + "_attack", 0.0),
        Hold = o.GetDouble(prefix + "_hold", 0.0),
        Decay = o.GetDouble(prefix + "_decay", 0.0),
        Sustain = AudioMath.Clamp(o.GetDouble(prefix + "_sustain", defaultSustain) / 100.0, 0.0, 1.0),
        Release = o.GetDouble(prefix + "_release", 0.0)
    };

    private static SamplerTrigger ParseTrigger(string? value) => value switch
    {
        "release" => SamplerTrigger.Release,
        "first" => SamplerTrigger.First,
        "legato" => SamplerTrigger.Legato,
        _ => SamplerTrigger.Attack
    };

    // Parses the trailing CC number from an opcode like "cutoff_cc74", or null if it doesn't match.
    private static int? ParseCcOpcode(string opcode, string prefix)
    {
        if (!opcode.StartsWith(prefix, StringComparison.Ordinal)) return null;
        return int.TryParse(opcode.AsSpan(prefix.Length), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static SamplerLoopMode ParseLoopMode(string? value) => value switch
    {
        "one_shot" => SamplerLoopMode.OneShot,
        "loop_continuous" => SamplerLoopMode.LoopContinuous,
        "loop_sustain" => SamplerLoopMode.LoopSustain,
        _ => SamplerLoopMode.NoLoop
    };

    private static FilterMode MapFilterType(string? filType) => filType switch
    {
        "hpf_1p" or "hpf_2p" or "hpf_4p" or "hpf_6p" => FilterMode.HighPass,
        "bpf_1p" or "bpf_2p" => FilterMode.BandPass,
        "brf_1p" or "brf_2p" => FilterMode.Notch,
        _ => FilterMode.LowPass
    };

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

    private static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;
    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
}
