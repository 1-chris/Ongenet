using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>How a region's sample repeats during playback (SFZ <c>loop_mode</c>).</summary>
public enum SfzLoopMode
{
    NoLoop,
    OneShot,        // play to the end ignoring note-off
    LoopContinuous, // loop start..end for the whole note
    LoopSustain     // loop while held; play through to the end after release
}

/// <summary>One parametric EQ band (SFZ <c>eqN_freq/gain/bw</c>).</summary>
public readonly record struct SfzEqBand(double Freq, double GainDb, double BandwidthOctaves);

/// <summary>What plays a region (SFZ <c>trigger</c>).</summary>
public enum SfzTrigger
{
    Attack,  // normal note-on (default)
    Release, // note-off (release samples)
    First,   // note-on only when no other notes are held
    Legato   // note-on only when other notes are already held
}

/// <summary>A CC→parameter mapping: <paramref name="Cc"/> at 127 contributes <paramref name="Depth"/>.</summary>
public readonly record struct SfzCcMod(int Cc, double Depth);

/// <summary>
/// Pre-computed, audio-thread-ready playback parameters for one <see cref="SfzRegion"/> bound to its
/// decoded sample. Built once at load so a voice never touches the opcode dictionary while rendering.
/// </summary>
public sealed class SfzRegionRuntime
{
    public required SfzSample Sample { get; init; }

    // Trigger geometry (MIDI units).
    public int LoKey { get; init; }
    public int HiKey { get; init; }
    public int LoVel { get; init; }
    public int HiVel { get; init; }

    // Pitch.
    public int PitchKeycenter { get; init; }
    public double KeytrackSemisPerKey { get; init; } // pitch_keytrack/100
    public double TransposeSemis { get; init; }
    public double TuneCents { get; init; }

    // Amplitude.
    public double Gain { get; init; }       // linear, from volume(dB) * amplitude%
    public double Pan { get; init; }        // -1..1
    public double AmpVeltrack { get; init; } // 0..100
    public SfzEgSpec AmpEg { get; init; }

    // Playback window + looping (frames).
    public long Offset { get; init; }
    public long End { get; init; }          // exclusive; clamped to sample length
    public SfzLoopMode LoopMode { get; init; }
    public long LoopStart { get; init; }
    public long LoopEnd { get; init; }      // exclusive
    public bool Reverse { get; init; }

    // Round-robin.
    public int SeqLength { get; init; } = 1;
    public int SeqPosition { get; init; } = 1;
    public int RoundRobinKey { get; init; }

    // Exclusive groups (used in the MIDI-control phase).
    public int Group { get; init; }
    public int OffBy { get; init; } = -1;

    // --- Tone shaping (Phase 3) ---

    // Resonant filter.
    public bool HasFilter { get; init; }
    public FilterMode FilterMode { get; init; }
    public double Cutoff { get; init; }          // Hz
    public double FilterQ { get; init; }
    public double FilKeytrack { get; init; }     // cents per key
    public int FilKeycenter { get; init; } = 60;
    public double FilVeltrack { get; init; }     // cents at full velocity

    // Filter envelope + LFO (modulate cutoff, in cents).
    public bool HasFilEg { get; init; }
    public double FilEgDepth { get; init; }
    public SfzEgSpec FilEg { get; init; }
    public bool HasFilLfo { get; init; }
    public double FilLfoFreq { get; init; }
    public double FilLfoDepth { get; init; }
    public double FilLfoDelay { get; init; }

    // Amplitude LFO (tremolo, depth in dB).
    public bool HasAmpLfo { get; init; }
    public double AmpLfoFreq { get; init; }
    public double AmpLfoDepthDb { get; init; }
    public double AmpLfoDelay { get; init; }

    // Pitch LFO (vibrato) + pitch envelope (both in cents).
    public bool HasPitchLfo { get; init; }
    public double PitchLfoFreq { get; init; }
    public double PitchLfoDepth { get; init; }
    public double PitchLfoDelay { get; init; }
    public bool HasPitchEg { get; init; }
    public double PitchEgDepth { get; init; }
    public SfzEgSpec PitchEg { get; init; }

    // Parametric EQ bands (applied after the filter).
    public IReadOnlyList<SfzEqBand> EqBands { get; init; } = System.Array.Empty<SfzEqBand>();

    // --- Articulation / control (Phase 4) ---

    /// <summary>How this region is triggered (attack/release/first/legato).</summary>
    public SfzTrigger Trigger { get; init; }

    /// <summary>Key-switch this region requires (sw_last/sw_down), or -1 for none.</summary>
    public int SwLast { get; init; } = -1;

    /// <summary>Key-switch zone bounds declared on this region (sw_lokey/sw_hikey), or -1.</summary>
    public int SwLoKey { get; init; } = -1;
    public int SwHiKey { get; init; } = -1;

    /// <summary>Initial key-switch (sw_default), or -1.</summary>
    public int SwDefault { get; init; } = -1;

    /// <summary>Pitch-bend range upward / downward magnitude, in cents (bend_up / |bend_down|).</summary>
    public double BendUpCents { get; init; } = 200;
    public double BendDownCents { get; init; } = 200;

    /// <summary>CC→cutoff modulations (cutoff_ccN), each contributing cents at CC 127.</summary>
    public IReadOnlyList<SfzCcMod> CutoffCc { get; init; } = System.Array.Empty<SfzCcMod>();

    /// <summary>True when any per-sample modulation/filtering is needed (else a voice uses the fast path).</summary>
    public bool ModActive => HasFilter || EqBands.Count > 0 || HasAmpLfo || HasPitchLfo || HasPitchEg || CutoffCc.Count > 0;

    public bool Matches(int key, int vel)
        => key >= LoKey && key <= HiKey && vel >= LoVel && vel <= HiVel;

    /// <summary>Builds a runtime from a parsed region and its decoded sample (null if the sample is missing).</summary>
    public static SfzRegionRuntime? Build(SfzRegion region, SfzSample? sample)
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
        var eqBands = new List<SfzEqBand>(3);
        for (var i = 1; i <= 3; i++)
        {
            var freq = o.GetDouble($"eq{i}_freq", -1);
            var gain = o.GetDouble($"eq{i}_gain", 0.0);
            if (freq > 0 && gain != 0.0)
                eqBands.Add(new SfzEqBand(freq, gain, o.GetDouble($"eq{i}_bw", 1.0)));
        }

        // CC → cutoff (cutoff_ccN / cutoff_onccN); each adds `value` cents at CC 127.
        var cutoffCc = new List<SfzCcMod>();
        foreach (var kv in o.Raw)
        {
            var cc = ParseCcOpcode(kv.Key, "cutoff_cc") ?? ParseCcOpcode(kv.Key, "cutoff_oncc");
            if (cc is { } n && double.TryParse(kv.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var cents) && cents != 0.0)
            {
                cutoffCc.Add(new SfzCcMod(n, cents));
            }
        }

        var bendDown = o.GetDouble("bend_down", -200.0);
        var swLast = o.GetKey("sw_last", o.GetKey("sw_down", -1));

        return new SfzRegionRuntime
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
            AmpEg = SfzEgSpec.Read(o, "ampeg", 100.0),
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
            FilEg = SfzEgSpec.Read(o, "fileg", 100.0),
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
            PitchEg = SfzEgSpec.Read(o, "pitcheg", 0.0),
            EqBands = eqBands,

            Trigger = ParseTrigger(o.Get("trigger")),
            SwLast = swLast,
            SwLoKey = o.GetKey("sw_lokey", -1),
            SwHiKey = o.GetKey("sw_hikey", -1),
            SwDefault = o.GetKey("sw_default", -1),
            BendUpCents = o.GetDouble("bend_up", 200.0),
            BendDownCents = System.Math.Abs(bendDown),
            CutoffCc = cutoffCc
        };
    }

    private static SfzTrigger ParseTrigger(string? value) => value switch
    {
        "release" => SfzTrigger.Release,
        "first" => SfzTrigger.First,
        "legato" => SfzTrigger.Legato,
        _ => SfzTrigger.Attack
    };

    // Parses the trailing CC number from an opcode like "cutoff_cc74", or null if it doesn't match.
    private static int? ParseCcOpcode(string opcode, string prefix)
    {
        if (!opcode.StartsWith(prefix, System.StringComparison.Ordinal)) return null;
        return int.TryParse(opcode.AsSpan(prefix.Length), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static SfzLoopMode ParseLoopMode(string? value) => value switch
    {
        "one_shot" => SfzLoopMode.OneShot,
        "loop_continuous" => SfzLoopMode.LoopContinuous,
        "loop_sustain" => SfzLoopMode.LoopSustain,
        _ => SfzLoopMode.NoLoop
    };

    private static FilterMode MapFilterType(string? filType) => filType switch
    {
        "hpf_1p" or "hpf_2p" or "hpf_4p" or "hpf_6p" => FilterMode.HighPass,
        "bpf_1p" or "bpf_2p" => FilterMode.BandPass,
        "brf_1p" or "brf_2p" => FilterMode.Notch,
        _ => FilterMode.LowPass
    };

    private static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;
    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
}
