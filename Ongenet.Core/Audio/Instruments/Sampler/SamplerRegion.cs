using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>How a region's sample repeats during playback.</summary>
public enum SamplerLoopMode
{
    NoLoop,
    OneShot,        // play to the end ignoring note-off
    LoopContinuous, // loop start..end for the whole note
    LoopSustain     // loop while held; play through to the end after release
}

/// <summary>One parametric EQ band.</summary>
public readonly record struct SamplerEqBand(double Freq, double GainDb, double BandwidthOctaves);

/// <summary>What plays a region.</summary>
public enum SamplerTrigger
{
    Attack,  // normal note-on (default)
    Release, // note-off (release samples)
    First,   // note-on only when no other notes are held
    Legato   // note-on only when other notes are already held
}

/// <summary>A CC→parameter mapping: <paramref name="Cc"/> at 127 contributes <paramref name="Depth"/>.</summary>
public readonly record struct SamplerCcMod(int Cc, double Depth);

/// <summary>
/// Pre-computed, audio-thread-ready playback parameters for one mapped sample zone, bound to its decoded
/// <see cref="SamplerSample"/>. This is the format-neutral region the engine plays: the SFZ loader builds
/// it from opcodes and the SF2 loader builds it from generators, but a <see cref="SamplerVoice"/> never
/// needs to know which format it came from. All properties are init-only so each loader constructs it
/// directly.
/// </summary>
public sealed class SamplerRegion
{
    public required SamplerSample Sample { get; init; }

    // Trigger geometry (MIDI units).
    public int LoKey { get; init; }
    public int HiKey { get; init; }
    public int LoVel { get; init; }
    public int HiVel { get; init; }

    // Pitch.
    public int PitchKeycenter { get; init; }
    public double KeytrackSemisPerKey { get; init; } // semitones per key (1.0 = normal chromatic tracking)
    public double TransposeSemis { get; init; }
    public double TuneCents { get; init; }

    // Amplitude.
    public double Gain { get; init; }       // linear
    public double Pan { get; init; }        // -1..1
    public double AmpVeltrack { get; init; } // 0..100
    public SamplerEgSpec AmpEg { get; init; }

    // Playback window + looping (frames).
    public long Offset { get; init; }
    public long End { get; init; }          // exclusive; clamped to sample length
    public SamplerLoopMode LoopMode { get; init; }
    public long LoopStart { get; init; }
    public long LoopEnd { get; init; }      // exclusive
    public bool Reverse { get; init; }

    // Round-robin.
    public int SeqLength { get; init; } = 1;
    public int SeqPosition { get; init; } = 1;
    public int RoundRobinKey { get; init; }

    // Exclusive groups.
    public int Group { get; init; }
    public int OffBy { get; init; } = -1;

    // --- Tone shaping ---

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
    public SamplerEgSpec FilEg { get; init; }
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
    public SamplerEgSpec PitchEg { get; init; }

    // Parametric EQ bands (applied after the filter).
    public IReadOnlyList<SamplerEqBand> EqBands { get; init; } = System.Array.Empty<SamplerEqBand>();

    // --- Articulation / control ---

    /// <summary>How this region is triggered (attack/release/first/legato).</summary>
    public SamplerTrigger Trigger { get; init; }

    /// <summary>Key-switch this region requires (SFZ sw_last/sw_down), or -1 for none.</summary>
    public int SwLast { get; init; } = -1;

    /// <summary>Key-switch zone bounds declared on this region (sw_lokey/sw_hikey), or -1.</summary>
    public int SwLoKey { get; init; } = -1;
    public int SwHiKey { get; init; } = -1;

    /// <summary>Initial key-switch (sw_default), or -1.</summary>
    public int SwDefault { get; init; } = -1;

    /// <summary>Pitch-bend range upward / downward magnitude, in cents.</summary>
    public double BendUpCents { get; init; } = 200;
    public double BendDownCents { get; init; } = 200;

    /// <summary>CC→cutoff modulations, each contributing cents at CC 127.</summary>
    public IReadOnlyList<SamplerCcMod> CutoffCc { get; init; } = System.Array.Empty<SamplerCcMod>();

    /// <summary>True when any per-sample modulation/filtering is needed (else a voice uses the fast path).</summary>
    public bool ModActive => HasFilter || EqBands.Count > 0 || HasAmpLfo || HasPitchLfo || HasPitchEg || CutoffCc.Count > 0;

    public bool Matches(int key, int vel)
        => key >= LoKey && key <= HiKey && vel >= LoVel && vel <= HiVel;
}
