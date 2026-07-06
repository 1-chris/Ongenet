using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Code-defined factory presets for instruments that don't implement <see cref="IPresetProvider"/>
/// (which covers only in-instrument preset pickers). Each definition is a deterministic builder that
/// returns a fully configured instrument; the preset library materializes them as factory
/// <c>.ongenpreset</c> files, and the preview song uses the same builders so the song and the library
/// presets stay identical.
/// </summary>
public static class FactoryPresets
{
    /// <summary>One materializable factory preset: which library group it lands in, its name, and its builder.</summary>
    public sealed record Definition(string InstrumentDisplayName, string PresetName, Func<IInstrument> Create);

    /// <summary>One materializable factory FX chain: a named, ready-to-drop insert chain.</summary>
    public sealed record ChainDefinition(string PresetName, Func<IAudioEffect[]> Create);

    public static IReadOnlyList<Definition> Definitions { get; } = new Definition[]
    {
        new("3x Osc", "Deep Sub Bass", DeepSubBass),
        new("3x Osc", "White Riser", WhiteRiser),
        new("3x Osc", "Trance Bass", TranceBass),
        new("3x Osc", "Reverse Cymbal", ReverseCymbal),
        new("FM Synth", "Glass Bells", GlassBells)
    };

    /// <summary>Factory FX chains for the library's FX Chains tab — well-matched effect stacks new
    /// users can drop straight onto a track.</summary>
    public static IReadOnlyList<ChainDefinition> ChainDefinitions { get; } = new ChainDefinition[]
    {
        new("Trance Lead Space", () => new IAudioEffect[]
        {
            new DelayEffect { TimeMs = 320, Feedback = 0.35, Mix = 0.25 },
            new ReverbEffect { Mix = 0.3, RoomSize = 0.8, Damping = 0.35 },
            new StereoWidthEffect { Width = 1.3 }
        }),
        new("Acid Machine", () => new IAudioEffect[]
        {
            new FilterEffect { Mode = FilterMode.LowPass, Frequency = 900, Resonance = 6.0 },
            new DistortionEffect { DriveDb = 10, Mix = 0.7 },
            new DelayEffect { TimeMs = 250, Feedback = 0.3, Mix = 0.18 }
        }),
        new("Lo-Fi Crush", () => new IAudioEffect[]
        {
            new BitcrusherEffect { Bits = 9, Downsample = 3, Mix = 0.8 },
            new FilterEffect { Mode = FilterMode.LowPass, Frequency = 3800, Resonance = 0.8 }
        }),
        new("Master Glue", () => new IAudioEffect[]
        {
            new CompressorEffect { ThresholdDb = -14, Ratio = 2.0, AttackMs = 30, ReleaseMs = 200, MakeupDb = 2 },
            new StereoWidthEffect { Width = 1.1 },
            new LimiterEffect { CeilingDb = -0.5, ReleaseMs = 110 }
        }),
        new("Pumping Pad", () => new IAudioEffect[]
        {
            new FilterEffect { Mode = FilterMode.LowPass, Frequency = 2500, Resonance = 0.8 },
            new SidechainEffect { Amount = 0.4, RateIndex = 2 },
            new StereoWidthEffect { Width = 1.4 }
        })
    };

    /// <summary>A round deep-house sub: sine fundamental + quiet octave-up triangle, dark low-pass.</summary>
    public static TripleOscInstrument DeepSubBass() => new()
    {
        Wave1 = (int)OscWave.Sine,
        Wave2 = (int)OscWave.Triangle, Coarse2 = 12, Level2 = 0.35,
        Wave3 = (int)OscWave.Saw, Level3 = 0.0,
        FilterTypeIndex = 1, Cutoff = 900, Resonance = 0.8,
        AttackSeconds = 0.004, DecaySeconds = 0.18, SustainLevel = 0.55, ReleaseSeconds = 0.09,
        Gain = 0.85
    };

    /// <summary>The rolling trance bass: a driving saw with a sub square an octave down, snapped
    /// tight by a short envelope under a dark low-pass — built for offbeat eighths.</summary>
    public static TripleOscInstrument TranceBass() => new()
    {
        Wave1 = (int)OscWave.Saw,
        Wave2 = (int)OscWave.Square, Coarse2 = -12, Level2 = 0.35,
        Wave3 = (int)OscWave.Saw, Level3 = 0.0,
        FilterTypeIndex = 1, Cutoff = 880, Resonance = 1.0,
        AttackSeconds = 0.002, DecaySeconds = 0.11, SustainLevel = 0.2, ReleaseSeconds = 0.05,
        Gain = 0.82
    };

    /// <summary>A reverse-cymbal swell: bright high-passed noise fading in over ~1.7 s (two bars at
    /// 140 BPM) and cutting off at the release — end a held note on a downbeat for the classic
    /// whoosh into a transition.</summary>
    public static TripleOscInstrument ReverseCymbal() => new()
    {
        Wave1 = (int)OscWave.Noise,
        Level2 = 0.0, Level3 = 0.0,
        FilterTypeIndex = 2, Cutoff = 4000, Resonance = 1.0,
        AttackSeconds = 1.7, DecaySeconds = 0.05, SustainLevel = 1.0, ReleaseSeconds = 0.12,
        Gain = 0.75
    };

    /// <summary>Glassy FM bells: a pure harmonic 2:1 modulator (chime, not clang) with a gentle
    /// strike and a long singing tail — made for doubling melody anchor notes an octave up.</summary>
    public static FmSynthInstrument GlassBells() => new()
    {
        ModRatio = 2.0,
        ModIndex = 1.5,
        AttackSeconds = 0.003,
        DecaySeconds = 1.1,
        SustainLevel = 0.0,
        ReleaseSeconds = 1.6
    };

    /// <summary>A noise riser bed: white noise + faint detuned saws behind a closed low-pass. The
    /// sweep itself comes from automating a filter insert (or this filter's cutoff) on the track.</summary>
    public static TripleOscInstrument WhiteRiser() => new()
    {
        Wave1 = (int)OscWave.Noise,
        Wave2 = (int)OscWave.Saw, Fine2 = 18, Level2 = 0.18,
        Wave3 = (int)OscWave.Saw, Fine3 = -18, Level3 = 0.18,
        FilterTypeIndex = 1, Cutoff = 800, Resonance = 2.0,
        AttackSeconds = 2.5, DecaySeconds = 1.0, SustainLevel = 1.0, ReleaseSeconds = 0.4,
        Gain = 0.7
    };
}
