using System;
using System.Collections.Generic;
using System.IO;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sf2;

/// <summary>
/// Loads a SoundFont 2 preset into format-neutral <see cref="SamplerRegion"/>s. Parses the file with
/// <see cref="Sf2Reader"/>, then flattens the chosen preset's two-level zone hierarchy
/// (preset → instrument → sample) into regions, applying the SF2 generator accumulation rules and
/// converting each generator into the engine's units:
/// <list type="bullet">
/// <item>Instrument-zone generators are <b>absolute</b> (seeded with the SF2 defaults, overridden per zone).</item>
/// <item>Preset-zone generators are <b>additive offsets</b> on top — except key/velocity ranges, which
/// <b>intersect</b>, and the instrument-only generators (sample addressing, looping, exclusive class,
/// root-key override, …), which the preset level never contributes to.</item>
/// </list>
/// SF2 samples are mono; a stereo voice is two mono samples (left/right) in separate zones, panned and
/// triggered together by the engine. All samples are loaded resident in RAM (SF2 banks are self-contained).
/// </summary>
public sealed class Sf2Loader
{
    public SamplerLoadResult? Load(string path, int presetIndex, IProgress<double>? progress)
    {
        Sf2File file;
        try { file = Sf2Reader.Parse(path); }
        catch { return null; }

        var order = file.PresetOrder;
        var presets = new List<SamplerPresetInfo>(order.Count);
        for (var i = 0; i < order.Count; i++)
            presets.Add(new SamplerPresetInfo(i, order[i].Bank, order[i].Program, order[i].Name));

        var warnings = new List<string>();
        var regions = new List<SamplerRegion>();
        var sampleCache = new Dictionary<int, SamplerSample>();

        var selected = -1;
        var display = Path.GetFileNameWithoutExtension(path);
        if (order.Count > 0)
        {
            selected = presetIndex < 0 || presetIndex >= order.Count ? 0 : presetIndex;
            var pref = order[selected];
            display = $"{Path.GetFileNameWithoutExtension(path)} - {pref.Name}";
            FlattenPreset(file, pref.PhdrIndex, regions, sampleCache, warnings);
        }
        else
        {
            warnings.Add("SoundFont contains no presets.");
        }

        progress?.Report(1.0);

        return new SamplerLoadResult
        {
            Regions = regions,
            Library = SamplerSampleLibrary.FromSamples(sampleCache.Values),
            Path = Path.GetFullPath(path),
            DisplayName = display,
            Format = SamplerFormat.Sf2,
            SourceText = string.Empty,
            PresetIndex = selected,
            Presets = presets,
            Warnings = warnings
        };
    }

    // Walks one preset's zones; each non-global zone points at an instrument to descend into.
    private static void FlattenPreset(Sf2File file, int phdrIndex, List<SamplerRegion> regions,
        Dictionary<int, SamplerSample> cache, List<string> warnings)
    {
        var zoneStart = file.Presets[phdrIndex].PresetBagNdx;
        var zoneEnd = file.Presets[phdrIndex + 1].PresetBagNdx;

        var presetGlobal = new Dictionary<Sf2Gen, Sf2GenItem>();
        for (var z = zoneStart; z < zoneEnd; z++)
        {
            var zoneGens = ZoneGens(file.PresetBags, file.PresetGens, z);
            if (!zoneGens.ContainsKey(Sf2Gen.Instrument))
            {
                if (z == zoneStart) presetGlobal = zoneGens; // leading global zone supplies defaults
                continue;
            }

            var presetGens = Merge(presetGlobal, zoneGens);
            var instIdx = presetGens[Sf2Gen.Instrument].Raw;
            if (instIdx >= file.Instruments.Count - 1) { warnings.Add("SF2: preset references an invalid instrument."); continue; }
            FlattenInstrument(file, instIdx, presetGens, regions, cache);
        }
    }

    // Walks one instrument's zones; each non-global zone points at a sample and becomes a region.
    private static void FlattenInstrument(Sf2File file, int instIdx,
        IReadOnlyDictionary<Sf2Gen, Sf2GenItem> presetGens, List<SamplerRegion> regions,
        Dictionary<int, SamplerSample> cache)
    {
        var zoneStart = file.Instruments[instIdx].BagNdx;
        var zoneEnd = file.Instruments[instIdx + 1].BagNdx;

        var instGlobal = new Dictionary<Sf2Gen, Sf2GenItem>();
        for (var z = zoneStart; z < zoneEnd; z++)
        {
            var zoneGens = ZoneGens(file.InstBags, file.InstGens, z);
            if (!zoneGens.ContainsKey(Sf2Gen.SampleID))
            {
                if (z == zoneStart) instGlobal = zoneGens;
                continue;
            }

            var instGens = Merge(instGlobal, zoneGens);
            var sampIdx = instGens[Sf2Gen.SampleID].Raw;
            if (sampIdx >= file.SampleHeaders.Count - 1) continue; // terminal / invalid

            var shdr = file.SampleHeaders[sampIdx];
            if (!cache.TryGetValue(sampIdx, out var sample))
            {
                var mono = file.ReadMono(shdr);
                if (mono.Length == 0) continue;
                var sr = shdr.SampleRate == 0 ? 44100 : (int)shdr.SampleRate;
                sample = SamplerSample.FromResident(new AudioSampleBuffer(mono, 1, sr));
                cache[sampIdx] = sample;
            }

            var region = BuildRegion(instGens, presetGens, shdr, sample, regions.Count);
            // Skip dead zones: when the preset range and instrument range don't overlap the intersection
            // is empty (lo > hi), so the region could never be triggered.
            if (region.LoKey <= region.HiKey && region.LoVel <= region.HiVel) regions.Add(region);
        }
    }

    /// <summary>
    /// Builds one playable region from a fully-resolved instrument generator set (absolute) and preset
    /// generator set (offsets), its sample header and decoded sample. Public + static so the accumulation
    /// and unit-conversion logic can be unit-tested directly.
    /// </summary>
    public static SamplerRegion BuildRegion(IReadOnlyDictionary<Sf2Gen, Sf2GenItem> instGens,
        IReadOnlyDictionary<Sf2Gen, Sf2GenItem> presetGens, in Sf2SampleHeader shdr, SamplerSample sample,
        int rrKey)
    {
        int InstVal(Sf2Gen g) => instGens.TryGetValue(g, out var it) ? it.Short : Sf2Convert.Default(g);
        int PresetOff(Sf2Gen g) => presetGens.TryGetValue(g, out var it) ? it.Short : 0;
        // Combined value: absolute instrument value, plus the preset's additive offset (unless the
        // generator is instrument-only, in which case the preset never contributes).
        int Comb(Sf2Gen g) => InstVal(g) + (Sf2Convert.IsInstrumentOnly(g) ? 0 : PresetOff(g));
        double Sec(Sf2Gen g) => Sf2Convert.TimecentsToSeconds(Comb(g));

        // Key/velocity ranges: instrument range narrowed by the preset's range (intersection).
        (int Lo, int Hi) Range(Sf2Gen g)
        {
            int lo = 0, hi = 127;
            if (instGens.TryGetValue(g, out var it)) { lo = it.Lo; hi = it.Hi; }
            if (presetGens.TryGetValue(g, out var pit)) { lo = Math.Max(lo, pit.Lo); hi = Math.Min(hi, pit.Hi); }
            return (lo, hi);
        }

        var (loKey, hiKey) = Range(Sf2Gen.KeyRange);
        var (loVel, hiVel) = Range(Sf2Gen.VelRange);

        // --- Pitch ---
        var rootGen = instGens.TryGetValue(Sf2Gen.OverridingRootKey, out var rk) ? rk.Short : -1;
        var root = rootGen >= 0 ? rootGen : shdr.OriginalPitch;
        if (root is < 0 or > 127) root = 60;
        var scale = Comb(Sf2Gen.ScaleTuning) / 100.0;
        var coarse = (double)Comb(Sf2Gen.CoarseTune);
        var fine = Comb(Sf2Gen.FineTune) + shdr.PitchCorrection;
        var keytrack = scale;
        var keynum = instGens.TryGetValue(Sf2Gen.Keynum, out var kn) ? kn.Short : -1;
        if (keynum >= 0) { coarse += (keynum - root) * scale; keytrack = 0; } // fixed-pitch zone (e.g. drums)

        // --- Amplitude ---
        var gain = Sf2Convert.AttenuationToGain(Comb(Sf2Gen.InitialAttenuation));
        var panSpecified = instGens.ContainsKey(Sf2Gen.Pan) || presetGens.ContainsKey(Sf2Gen.Pan);
        var pan = Math.Clamp(Comb(Sf2Gen.Pan) / 500.0, -1.0, 1.0);
        if (!panSpecified)
        {
            if (shdr.SampleType == 4) pan = -1.0;      // left
            else if (shdr.SampleType == 2) pan = 1.0;  // right
        }

        var forcedVel = instGens.TryGetValue(Sf2Gen.Velocity, out var fv) && fv.Short >= 0;
        var ampVeltrack = forcedVel ? 0.0 : 100.0; // SF2's default velocity→attenuation modulator (concave)

        var ampEg = new SamplerEgSpec
        {
            Delay = Sec(Sf2Gen.DelayVolEnv),
            Attack = Sec(Sf2Gen.AttackVolEnv),
            Hold = Sec(Sf2Gen.HoldVolEnv),
            Decay = Sec(Sf2Gen.DecayVolEnv),
            Sustain = Sf2Convert.SustainCentibelsToLevel(Comb(Sf2Gen.SustainVolEnv)),
            Release = Sec(Sf2Gen.ReleaseVolEnv)
        };

        // --- Filter ---
        var fc = Comb(Sf2Gen.InitialFilterFc);
        var hasFilter = fc < 13500;
        var cutoff = Math.Clamp(Sf2Convert.AbsoluteCentsToHz(fc), 20.0, 20000.0);
        var filterQ = 0.70710678 * Math.Pow(10.0, Comb(Sf2Gen.InitialFilterQ) / 200.0);

        // --- Modulation envelope → filter + pitch ---
        var modEnvToFc = Comb(Sf2Gen.ModEnvToFilterFc);
        var modEnvToPitch = Comb(Sf2Gen.ModEnvToPitch);
        var modEg = new SamplerEgSpec
        {
            Delay = Sec(Sf2Gen.DelayModEnv),
            Attack = Sec(Sf2Gen.AttackModEnv),
            Hold = Sec(Sf2Gen.HoldModEnv),
            Decay = Sec(Sf2Gen.DecayModEnv),
            Sustain = Math.Clamp(1.0 - Comb(Sf2Gen.SustainModEnv) / 1000.0, 0.0, 1.0),
            Release = Sec(Sf2Gen.ReleaseModEnv)
        };
        var hasFilEg = hasFilter && modEnvToFc != 0;
        var hasPitchEg = modEnvToPitch != 0;

        // --- Mod LFO (pitch + filter + volume) ---
        var modLfoHz = Sf2Convert.AbsoluteCentsToHz(Comb(Sf2Gen.FreqModLFO));
        var modLfoDelay = Sec(Sf2Gen.DelayModLFO);
        var modToVol = Comb(Sf2Gen.ModLfoToVolume);
        var modToFc = Comb(Sf2Gen.ModLfoToFilterFc);
        var modToPitch = Comb(Sf2Gen.ModLfoToPitch);
        var hasAmpLfo = modToVol != 0;
        var hasFilLfo = hasFilter && modToFc != 0;

        // --- Vib LFO (pitch). One pitch LFO slot: vibrato wins; else the mod-LFO's pitch depth. ---
        var vibLfoHz = Sf2Convert.AbsoluteCentsToHz(Comb(Sf2Gen.FreqVibLFO));
        var vibLfoDelay = Sec(Sf2Gen.DelayVibLFO);
        var vibToPitch = Comb(Sf2Gen.VibLfoToPitch);
        bool hasPitchLfo;
        double pLfoFreq = 0, pLfoDepth = 0, pLfoDelay = 0;
        if (vibToPitch != 0) { hasPitchLfo = true; pLfoFreq = vibLfoHz; pLfoDepth = vibToPitch; pLfoDelay = vibLfoDelay; }
        else if (modToPitch != 0) { hasPitchLfo = true; pLfoFreq = modLfoHz; pLfoDepth = modToPitch; pLfoDelay = modLfoDelay; }
        else hasPitchLfo = false;

        // --- Sample window + loop (instrument-only sample-address generators) ---
        var bufFrames = sample.FrameCount;
        var offset = Clamp(InstVal(Sf2Gen.StartAddrsOffset) + 32768L * InstVal(Sf2Gen.StartAddrsCoarseOffset), 0, bufFrames);
        var end = Clamp(bufFrames + InstVal(Sf2Gen.EndAddrsOffset) + 32768L * InstVal(Sf2Gen.EndAddrsCoarseOffset), 0, bufFrames);
        var loopStart = Clamp((long)(shdr.StartLoop - shdr.Start)
            + InstVal(Sf2Gen.StartloopAddrsOffset) + 32768L * InstVal(Sf2Gen.StartloopAddrsCoarseOffset), 0, bufFrames);
        var loopEnd = Clamp((long)(shdr.EndLoop - shdr.Start)
            + InstVal(Sf2Gen.EndloopAddrsOffset) + 32768L * InstVal(Sf2Gen.EndloopAddrsCoarseOffset), 0, bufFrames);

        var modes = InstVal(Sf2Gen.SampleModes) & 3;
        var loopMode = modes switch
        {
            1 => SamplerLoopMode.LoopContinuous,
            3 => SamplerLoopMode.LoopSustain,
            _ => SamplerLoopMode.NoLoop
        };

        var excl = InstVal(Sf2Gen.ExclusiveClass);

        return new SamplerRegion
        {
            Sample = sample,
            LoKey = loKey,
            HiKey = hiKey,
            LoVel = loVel,
            HiVel = hiVel,
            PitchKeycenter = root,
            KeytrackSemisPerKey = keytrack,
            TransposeSemis = coarse,
            TuneCents = fine,
            Gain = gain,
            Pan = pan,
            AmpVeltrack = ampVeltrack,
            AmpEg = ampEg,
            Offset = offset,
            End = end,
            LoopMode = loopMode,
            LoopStart = loopStart,
            LoopEnd = loopEnd > loopStart ? loopEnd : end,
            Reverse = false,
            SeqLength = 1,
            SeqPosition = 1,
            RoundRobinKey = rrKey,
            Group = excl > 0 ? excl : 0,
            OffBy = excl > 0 ? excl : -1,

            HasFilter = hasFilter,
            FilterMode = FilterMode.LowPass,
            Cutoff = cutoff,
            FilterQ = filterQ,
            HasFilEg = hasFilEg,
            FilEgDepth = modEnvToFc,
            FilEg = modEg,
            HasFilLfo = hasFilLfo,
            FilLfoFreq = modLfoHz,
            FilLfoDepth = modToFc,
            FilLfoDelay = modLfoDelay,
            HasAmpLfo = hasAmpLfo,
            AmpLfoFreq = modLfoHz,
            AmpLfoDepthDb = modToVol / 10.0,
            AmpLfoDelay = modLfoDelay,
            HasPitchLfo = hasPitchLfo,
            PitchLfoFreq = pLfoFreq,
            PitchLfoDepth = pLfoDepth,
            PitchLfoDelay = pLfoDelay,
            HasPitchEg = hasPitchEg,
            PitchEgDepth = modEnvToPitch,
            PitchEg = modEg,

            Trigger = SamplerTrigger.Attack,
            BendUpCents = 200,
            BendDownCents = 200
        };
    }

    // Reads a zone's generators into a last-wins map (a repeated operator takes its final value).
    private static Dictionary<Sf2Gen, Sf2GenItem> ZoneGens(
        IReadOnlyList<Sf2Bag> bags, IReadOnlyList<Sf2GenItem> gens, int zone)
    {
        var start = bags[zone].GenNdx;
        var end = zone + 1 < bags.Count ? bags[zone + 1].GenNdx : gens.Count;
        var dict = new Dictionary<Sf2Gen, Sf2GenItem>();
        for (var g = start; g < end && g < gens.Count; g++) dict[gens[g].Oper] = gens[g];
        return dict;
    }

    // Merges a global zone (defaults) with a specific zone that overrides it.
    private static Dictionary<Sf2Gen, Sf2GenItem> Merge(
        Dictionary<Sf2Gen, Sf2GenItem> global, Dictionary<Sf2Gen, Sf2GenItem> zone)
    {
        if (global.Count == 0) return zone;
        var merged = new Dictionary<Sf2Gen, Sf2GenItem>(global);
        foreach (var kv in zone) merged[kv.Key] = kv.Value;
        return merged;
    }

    private static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;
}
