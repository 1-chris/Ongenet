using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Code-defined factory presets for instruments that don't implement <see cref="IPresetProvider"/>
/// (which covers only in-instrument preset pickers). Each definition is a deterministic builder that
/// returns a fully configured instrument; the preset library materializes them as factory
/// <c>.ongenpreset</c> files, and demo songs use the same builders so the song and the library
/// presets stay identical.
/// </summary>
public static class FactoryPresets
{
    /// <summary>One materializable factory preset: which library group it lands in, its name, builder, and FLEX-style tags.</summary>
    public sealed record Definition(string InstrumentDisplayName, string PresetName, Func<IInstrument> Create, string[]? tags = null)
    {
        public string[] Tags { get; } = tags ?? Array.Empty<string>();
    }

    /// <summary>One materializable factory FX chain: a named, ready-to-drop insert chain.</summary>
    public sealed record ChainDefinition(string PresetName, Func<IAudioEffect[]> Create, string[]? tags = null)
    {
        public string[] Tags { get; } = tags ?? Array.Empty<string>();
    }

    /// <summary>One materializable single-effect factory preset for the FX Presets library tab.</summary>
    public sealed record EffectDefinition(string EffectDisplayName, string PresetName, Func<IAudioEffect> Create, string[]? tags = null)
    {
        public string[] Tags { get; } = tags ?? Array.Empty<string>();
    }

    public static IReadOnlyList<Definition> Definitions { get; } = new Definition[]
    {
        // 3x Osc
        new("3x Osc", "Deep Sub Bass", DeepSubBass, ["bass", "sub", "synth"]),
        new("3x Osc", "White Riser", WhiteRiser, ["fx", "riser", "synth"]),
        new("3x Osc", "Trance Bass", TranceBass, ["bass", "trance", "synth"]),
        new("3x Osc", "Reverse Cymbal", ReverseCymbal),
        new("3x Osc", "Pluck Stack", PluckStack),
        new("3x Osc", "Super Saw Lead", SuperSawLead, ["lead", "trance", "synth"]),
        new("3x Osc", "Reese Growl", ReeseGrowl, ["bass", "reese", "synth"]),
        new("3x Osc", "Acid Square", AcidSquare, ["bass", "acid", "synth"]),
        new("3x Osc", "Soft Keys", SoftKeys, ["keys", "synth"]),
        new("3x Osc", "Detuned Pad", DetunedPad, ["pad", "synth"]),
        new("3x Osc", "Hoover Stab", HooverStab),
        new("3x Osc", "Organ Drawbar", OrganDrawbar),

        // FM Synth
        new("FM Synth", "Glass Bells", GlassBells),
        new("FM Synth", "Warm Pad", WarmPad),
        new("FM Synth", "Electric Piano", ElectricPiano),
        new("FM Synth", "Metallic Hit", MetallicHit),
        new("FM Synth", "Bass Growl FM", BassGrowlFm),
        new("FM Synth", "Crystal Pluck FM", CrystalPluckFm),
        new("FM Synth", "Soft Clarinet", SoftClarinet),
        new("FM Synth", "Bright Stab FM", BrightStabFm),

        // Bass Synth
        new("Bass Synth", "Deep Sub", BassDeepSub),
        new("Bass Synth", "Reese", BassReese),
        new("Bass Synth", "Acid Pulse", BassAcidPulse),
        new("Bass Synth", "Warm Square", BassWarmSquare),
        new("Bass Synth", "Plucky Bass", BassPlucky),
        new("Bass Synth", "Growl Drive", BassGrowlDrive),
        new("Bass Synth", "Soft Sine", BassSoftSine),
        new("Bass Synth", "Funky Slap", BassFunkySlap),

        // Oscillator
        new("Oscillator", "Classic Saw", OscClassicSaw),
        new("Oscillator", "Pure Sine", OscPureSine),
        new("Oscillator", "Pulse Lead", OscPulseLead),
        new("Oscillator", "Soft Pad", OscSoftPad),
        new("Oscillator", "Pluck", OscPluck),
        new("Oscillator", "Organ Tone", OscOrganTone),
        new("Oscillator", "Bass Sine", OscBassSine),
        new("Oscillator", "Square Keys", OscSquareKeys),

        // Wavetable
        new("Wavetable", "Basic Init", WtBasicInit),
        new("Wavetable", "Moving Pad", WtMovingPad),
        new("Wavetable", "Gritty Bass", WtGrittyBass),
        new("Wavetable", "Glass Lead", WtGlassLead),
        new("Wavetable", "Wide Unison", WtWideUnison),
        new("Wavetable", "Pluck Morph", WtPluckMorph),
        new("Wavetable", "Dark Sweep", WtDarkSweep),
        new("Wavetable", "Air Keys", WtAirKeys),

        // Granular (param defaults; best with a loaded sample)
        new("Granular", "Texture Cloud", GranTextureCloud),
        new("Granular", "Stretched Pad", GranStretchedPad),
        new("Granular", "Glitch Spray", GranGlitchSpray),
        new("Granular", "Reverse Haze", GranReverseHaze),
        new("Granular", "Dense Swarm", GranDenseSwarm),
        new("Granular", "Sparse Drops", GranSparseDrops),
        new("Granular", "Pitch Wash", GranPitchWash),
        new("Granular", "Loop Scrape", GranLoopScrape),

        // Basic Sampler envelopes
        new("Basic Sampler", "One Shot Tight", SamplerTight),
        new("Basic Sampler", "Pad Soft", SamplerPadSoft),
        new("Basic Sampler", "Pluck Release", SamplerPluck),
        new("Basic Sampler", "Sustained Soft", SamplerSustain),
        new("Basic Sampler", "Long Tail", SamplerLongTail),
        new("Basic Sampler", "Punch Hit", SamplerPunch),
        new("Basic Sampler", "Ambient Bloom", SamplerAmbient),
        new("Basic Sampler", "Transient Snap", SamplerSnap),
        new("Basic Sampler", "Plucked String", SamplerPluckedString),

        // Organ
        new("Organ", "Classic 888", OrganClassic888),
        new("Organ", "Jazz Drawbars", OrganJazzDrawbars),
        new("Organ", "Gospel Full", OrganGospelFull),
        new("Organ", "Rock B3", OrganRockB3),
        new("Organ", "Soft Cathedral", OrganSoftCathedral),
        new("Organ", "Percussive Stab", OrganPercussiveStab),
        new("Organ", "Vibrato Warm", OrganVibratoWarm),
        new("Organ", "Minimal Sub", OrganMinimalSub),

        // Phase-4
        new("Phase-4", "FM Bell", Phase4FmBell),
        new("Phase-4", "Digital Pad", Phase4DigitalPad),
        new("Phase-4", "Reso Lead", Phase4ResoLead),
        new("Phase-4", "Harsh Bass", Phase4HarshBass),
        new("Phase-4", "Glass Pluck", Phase4GlassPluck),
        new("Phase-4", "Warm Stack", Phase4WarmStack),
        new("Phase-4", "Metallic Keys", Phase4MetallicKeys),
        new("Phase-4", "CZ Brass", Phase4CzBrass),

        // Polysynth
        new("Polysynth", "Init", PolysynthInit),
        new("Polysynth", "Bright Lead", PolysynthBrightLead),
        new("Polysynth", "Warm Pad", PolysynthWarmPad),
        new("Polysynth", "Acid Bass", PolysynthAcidBass),
        new("Polysynth", "Pluck", PolysynthPluck),
        new("Polysynth", "Noise Sweep", PolysynthNoiseSweep),

        // Polymer
        new("Polymer", "Init", PolymerInit),
        new("Polymer", "Glass Pad", PolymerGlassPad),
        new("Polymer", "Detuned Stack", PolymerDetunedStack),
        new("Polymer", "Soft Keys", PolymerSoftKeys),
        new("Polymer", "Filter Wobble", PolymerFilterWobble),

        // Drum Model
        new("Drum Model", "808 Kick", Drum808Kick),
        new("Drum Model", "909 Snare", Drum909Snare),
        new("Drum Model", "Tight Hat", DrumTightHat),
        new("Drum Model", "Room Clap", DrumRoomClap),
        new("Drum Model", "Techno Zap", DrumTechnoZap),
        new("Drum Model", "Lo-Fi Tom", DrumLoFiTom),
        new("Drum Model", "Vintage Cymbal", DrumVintageCymbal),
        new("Drum Model", "Punch Rim", DrumPunchRim),
    };

    public static IReadOnlyList<EffectDefinition> EffectDefinitions { get; } =
    [
        new("EQ", "Vocal Clarity", VocalClarityEq),
        new("EQ", "Kick Boom Cut", KickBoomCutEq),
        new("EQ", "Air Lift", AirLiftEq),
        new("EQ", "Mud Clean", MudCleanEq),
        new("Compressor", "Sidechain Pump Comp", SidechainPumpComp),
        new("Compressor", "Vocal Gentle", VocalGentleComp),
        new("Compressor", "Drum Bus Smash", DrumBusSmashComp),
        new("Compressor", "Glue Soft", GlueSoftComp),
        new("Limiter", "Club Master", ClubMasterLimiter),
        new("Limiter", "Safe Peak", SafePeakLimiter),
        new("Limiter", "Broadcast Hot", BroadcastHotLimiter),
        new("Distortion", "Tape Warmth", TapeWarmthDist),
        new("Distortion", "Amp Crunch", AmpCrunchDist),
        new("Distortion", "Fuzz Edge", FuzzEdgeDist),
        new("Exciter", "Warm Air", ExciterWarmAir),
        new("Exciter", "Aggressive Edge", ExciterAggressiveEdge),
        new("Exciter", "Subtle Presence", ExciterSubtlePresence),
        new("Filter", "LP Sweep Dark", LpSweepDark),
        new("Filter", "HP Clean", HpClean),
        new("Filter", "Reso Peak", ResoPeak),
        new("Delay", "Dub Delay", DubDelay),
        new("Delay", "Slapback", SlapbackDelay),
        new("Delay", "Ping Pong Wide", PingPongWideDelay),
        new("Reverb", "Hall Plate", HallPlateReverb),
        new("Reverb", "Room Small", RoomSmallReverb),
        new("Reverb", "Cathedral Wash", CathedralWashReverb),
        new("Chorus", "Wide Pad Chorus", WidePadChorus),
        new("Chorus", "Subtle Widen", SubtleWidenChorus),
        new("Phaser", "Slow Sweep", SlowSweepPhaser),
        new("Flanger", "Jet Whoosh", JetWhooshFlanger),
        new("Bitcrusher", "Lo-Fi Crush", LofiCrush),
        new("Stereo Width", "Wide Stage", WideStage),
        new("Stuttero", "Auto Performance", StutteroAuto),
        new("Gate", "Tight Noise Gate", TightNoiseGate),
        new("Sidechain", "Classic Pump", ClassicPumpSidechain),
        new("Tremolo", "Slow Pulse", SlowPulseTremolo),
        new("Clipper", "Transient Soft", TransientSoftClip),
        new("Convolution", "Small Room", ConvolutionSmallRoom),
        new("Convolution", "Hall Wash", ConvolutionHallWash),
        new("Convolution", "Plate Bright", ConvolutionPlateBright),
        new("Rotary", "Slow Leslie", RotarySlowLeslie),
        new("Rotary", "Fast Spin", RotaryFastSpin),
        new("Rotary", "Drive Warm", RotaryDriveWarm),
        new("Dynamics", "Vocal Comp", DynamicsVocalComp),
        new("Dynamics", "Drum Punch", DynamicsDrumPunch),
        new("Dynamics", "Expand Gate", DynamicsExpandGate),
        new("Delay+", "Dub Echo", DelayPlusDubEcho),
        new("Delay+", "Modulated Ping", DelayPlusModulatedPing),
        new("Delay+", "Filtered Slap", DelayPlusFilteredSlap),
    ];

    public static IReadOnlyList<ChainDefinition> ChainDefinitions { get; } =
    [
        new("Trance Lead Space", () =>
        [
            new DelayEffect { TimeMs = 320, Feedback = 0.35, Mix = 0.25 },
            new ReverbEffect { Mix = 0.3, RoomSize = 0.8, Damping = 0.35 },
            new StereoWidthEffect { Width = 1.3 }
        ]),
        new("Acid Machine", () =>
        [
            new FilterEffect { Mode = FilterMode.LowPass, Frequency = 900, Resonance = 6.0 },
            new DistortionEffect { DriveDb = 10, Mix = 0.7 },
            new DelayEffect { TimeMs = 250, Feedback = 0.3, Mix = 0.18 }
        ]),
        new("Lo-Fi Crush", () =>
        [
            new BitcrusherEffect { Bits = 9, Downsample = 3, Mix = 0.8 },
            new FilterEffect { Mode = FilterMode.LowPass, Frequency = 3800, Resonance = 0.8 }
        ]),
        new("Master Glue", () =>
        [
            new CompressorEffect { ThresholdDb = -14, Ratio = 2.0, AttackMs = 30, ReleaseMs = 200, MakeupDb = 2 },
            new StereoWidthEffect { Width = 1.1 },
            new LimiterEffect { CeilingDb = -0.5, ReleaseMs = 110 }
        ]),
        new("Pumping Pad", () =>
        [
            new FilterEffect { Mode = FilterMode.LowPass, Frequency = 2500, Resonance = 0.8 },
            new SidechainEffect { Amount = 0.4, RateIndex = 2 },
            new StereoWidthEffect { Width = 1.4 }
        ]),
        new("Drum Bus Punch", () =>
        [
            new FilterEffect { Mode = FilterMode.HighPass, Frequency = 55, Resonance = 0.5 },
            new CompressorEffect { ThresholdDb = -18, Ratio = 3.0, AttackMs = 8, ReleaseMs = 120, MakeupDb = 3 },
            new LimiterEffect { CeilingDb = -1.0, ReleaseMs = 80 }
        ]),
        new("Vocal Bus", () =>
        [
            VocalClarityEq(),
            new CompressorEffect { ThresholdDb = -16, Ratio = 3.0, AttackMs = 12, ReleaseMs = 100, MakeupDb = 2 },
            DeEssishEq(),
            new ReverbEffect { Mix = 0.12, RoomSize = 0.45, Damping = 0.55 }
        ]),
        new("Guitar Amp Sim", () =>
        [
            new FilterEffect { Mode = FilterMode.HighPass, Frequency = 80, Resonance = 0.5 },
            new DistortionEffect { DriveDb = 18, Mix = 0.85, Mode = 1 },
            EqEffectBands(new EqBand(EqBandType.Bell, 400, -3, 1.0), new EqBand(EqBandType.HighShelf, 5000, 2, 0.7)),
            new DelayEffect { TimeMs = 380, Feedback = 0.25, Mix = 0.15 }
        ]),
        new("Techno Master", () =>
        [
            new FilterEffect { Mode = FilterMode.HighPass, Frequency = 30, Resonance = 0.4 },
            new MultibandCompressorEffect(),
            new StereoWidthEffect { Width = 1.15 },
            new LimiterEffect { CeilingDb = -0.3, ReleaseMs = 90 }
        ]),
        new("Podcast Voice", () =>
        [
            new FilterEffect { Mode = FilterMode.HighPass, Frequency = 90, Resonance = 0.5 },
            VocalClarityEq(),
            new CompressorEffect { ThresholdDb = -20, Ratio = 4.0, AttackMs = 8, ReleaseMs = 80, MakeupDb = 4 },
            new LimiterEffect { CeilingDb = -1.0, ReleaseMs = 60 }
        ]),
        new("Bass Tighten", () =>
        [
            new FilterEffect { Mode = FilterMode.LowPass, Frequency = 180, Resonance = 0.6 },
            new CompressorEffect { ThresholdDb = -16, Ratio = 3.5, AttackMs = 20, ReleaseMs = 140, MakeupDb = 2 },
            new DistortionEffect { DriveDb = 4, Mix = 0.25 }
        ]),
        new("Ambient Space", () =>
        [
            new ChorusEffect { Mix = 0.35, RateHz = 0.25, Depth = 0.5 },
            new DelayEffect { TimeMs = 520, Feedback = 0.45, Mix = 0.3, PingPong = true },
            new ReverbEffect { Mix = 0.5, RoomSize = 0.92, Damping = 0.25 }
        ]),
    ];

    // ---- Helper EQ builders ----

    private static EqEffect VocalClarityEq()
    {
        var eq = new EqEffect();
        ClearAndAdd(eq,
            new EqBand(EqBandType.HighPass, 90, 0, 0.7),
            new EqBand(EqBandType.Bell, 280, -2.5, 1.0),
            new EqBand(EqBandType.Bell, 3500, 2.5, 0.9),
            new EqBand(EqBandType.HighShelf, 10000, 1.5, 0.7));
        return eq;
    }

    private static EqEffect DeEssishEq()
    {
        var eq = new EqEffect();
        ClearAndAdd(eq, new EqBand(EqBandType.Bell, 6500, -3.5, 1.5));
        return eq;
    }

    private static EqEffect KickBoomCutEq()
    {
        var eq = new EqEffect();
        ClearAndAdd(eq,
            new EqBand(EqBandType.Bell, 250, -4, 1.2),
            new EqBand(EqBandType.Bell, 60, 2, 0.8),
            new EqBand(EqBandType.HighShelf, 8000, -1, 0.7));
        return eq;
    }

    private static EqEffect AirLiftEq()
    {
        var eq = new EqEffect();
        ClearAndAdd(eq, new EqBand(EqBandType.HighShelf, 12000, 3.5, 0.7));
        return eq;
    }

    private static EqEffect MudCleanEq()
    {
        var eq = new EqEffect();
        ClearAndAdd(eq,
            new EqBand(EqBandType.HighPass, 40, 0, 0.7),
            new EqBand(EqBandType.Bell, 320, -3, 1.1));
        return eq;
    }

    private static EqEffect EqEffectBands(params EqBand[] bands)
    {
        var eq = new EqEffect();
        ClearAndAdd(eq, bands);
        return eq;
    }

    private static void ClearAndAdd(EqEffect eq, params EqBand[] bands)
    {
        while (eq.Bands.Count > 0) eq.RemoveBand(eq.Bands[0]);
        foreach (var b in bands) eq.AddBand(b);
    }

    // ---- Effect builders ----

    private static CompressorEffect SidechainPumpComp() => new()
        { ThresholdDb = -20, Ratio = 6, AttackMs = 5, ReleaseMs = 180, MakeupDb = 3 };

    private static CompressorEffect VocalGentleComp() => new()
        { ThresholdDb = -18, Ratio = 2.5, AttackMs = 15, ReleaseMs = 120, MakeupDb = 2 };

    private static CompressorEffect DrumBusSmashComp() => new()
        { ThresholdDb = -22, Ratio = 5, AttackMs = 4, ReleaseMs = 90, MakeupDb = 4 };

    private static CompressorEffect GlueSoftComp() => new()
        { ThresholdDb = -12, Ratio = 1.8, AttackMs = 40, ReleaseMs = 220, MakeupDb = 1 };

    private static LimiterEffect ClubMasterLimiter() => new() { CeilingDb = -0.3, ReleaseMs = 80 };
    private static LimiterEffect SafePeakLimiter() => new() { CeilingDb = -1.0, ReleaseMs = 100 };
    private static LimiterEffect BroadcastHotLimiter() => new() { CeilingDb = -0.1, ReleaseMs = 60 };

    private static DistortionEffect TapeWarmthDist() => new() { DriveDb = 6, Mix = 0.35, Mode = 0 };
    private static DistortionEffect AmpCrunchDist() => new() { DriveDb = 16, Mix = 0.7, Mode = 1 };
    private static DistortionEffect FuzzEdgeDist() => new() { DriveDb = 24, Mix = 0.55, Mode = 2 };

    private static ExciterEffect ExciterWarmAir() => new()
        { Drive = 6, Mix = 0.28, ToneHz = 4200, Mode = (int)ShaperType.Tanh, OutputDb = 0 };
    private static ExciterEffect ExciterAggressiveEdge() => new()
        { Drive = 14, Mix = 0.55, ToneHz = 2800, Mode = (int)ShaperType.Foldback, OutputDb = -1.5 };
    private static ExciterEffect ExciterSubtlePresence() => new()
        { Drive = 3.5, Mix = 0.18, ToneHz = 6500, Mode = (int)ShaperType.Tanh, OutputDb = 0.5 };

    private static FilterEffect LpSweepDark() => new()
        { Mode = FilterMode.LowPass, Frequency = 1200, Resonance = 2.5 };
    private static FilterEffect HpClean() => new()
        { Mode = FilterMode.HighPass, Frequency = 120, Resonance = 0.5 };
    private static FilterEffect ResoPeak() => new()
        { Mode = FilterMode.BandPass, Frequency = 1800, Resonance = 6.0 };

    private static DelayEffect DubDelay() => new()
        { TimeMs = 450, Feedback = 0.55, Mix = 0.4 };
    private static DelayEffect SlapbackDelay() => new()
        { TimeMs = 95, Feedback = 0.1, Mix = 0.28 };
    private static DelayEffect PingPongWideDelay() => new()
        { TimeMs = 380, Feedback = 0.4, Mix = 0.32, PingPong = true };

    private static ReverbEffect HallPlateReverb() => new()
        { Mix = 0.35, RoomSize = 0.75, Damping = 0.4, Width = 1.0 };
    private static ReverbEffect RoomSmallReverb() => new()
        { Mix = 0.18, RoomSize = 0.35, Damping = 0.6, Width = 0.8 };
    private static ReverbEffect CathedralWashReverb() => new()
        { Mix = 0.5, RoomSize = 0.95, Damping = 0.2, Width = 1.0 };

    private static ChorusEffect WidePadChorus() => new() { Mix = 0.45, RateHz = 0.35, Depth = 0.6 };
    private static ChorusEffect SubtleWidenChorus() => new() { Mix = 0.2, RateHz = 0.4, Depth = 0.35 };

    private static PhaserEffect SlowSweepPhaser() => new() { RateHz = 0.2, Depth = 0.7, Feedback = 0.35, Mix = 0.5 };
    private static FlangerEffect JetWhooshFlanger() => new() { RateHz = 0.35, Depth = 0.8, Feedback = 0.5, Mix = 0.45 };
    private static BitcrusherEffect LofiCrush() => new() { Bits = 8, Downsample = 4, Mix = 0.75 };
    private static StereoWidthEffect WideStage() => new() { Width = 1.5 };
    private static StutteroEffect StutteroAuto() => new() { ModeIndex = 0, Mix = 1.0, AutoGestureIndex = 0 };
    private static GateEffect TightNoiseGate() => new() { ThresholdDb = -40, AttackMs = 1, ReleaseMs = 40 };
    private static SidechainEffect ClassicPumpSidechain() => new() { Amount = 0.55, RateIndex = 2 };
    private static TremoloEffect SlowPulseTremolo() => new() { RateHz = 2.5, Depth = 0.45 };
    private static ClipperEffect TransientSoftClip() => new() { CeilingDb = -1.5 };

    // ---- Instrument builders (existing + new) ----

    public static TripleOscInstrument DeepSubBass() => new()
    {
        Wave1 = (int)OscWave.Sine,
        Wave2 = (int)OscWave.Triangle, Coarse2 = 12, Level2 = 0.35,
        Wave3 = (int)OscWave.Saw, Level3 = 0.0,
        FilterTypeIndex = 1, Cutoff = 900, Resonance = 0.8,
        AttackSeconds = 0.004, DecaySeconds = 0.18, SustainLevel = 0.55, ReleaseSeconds = 0.09,
        Gain = 0.85
    };

    public static TripleOscInstrument TranceBass() => new()
    {
        Wave1 = (int)OscWave.Saw,
        Wave2 = (int)OscWave.Square, Coarse2 = -12, Level2 = 0.35,
        Wave3 = (int)OscWave.Saw, Level3 = 0.0,
        FilterTypeIndex = 1, Cutoff = 880, Resonance = 1.0,
        AttackSeconds = 0.002, DecaySeconds = 0.11, SustainLevel = 0.2, ReleaseSeconds = 0.05,
        Gain = 0.82
    };

    public static TripleOscInstrument ReverseCymbal() => new()
    {
        Wave1 = (int)OscWave.Noise,
        Level2 = 0.0, Level3 = 0.0,
        FilterTypeIndex = 2, Cutoff = 4000, Resonance = 1.0,
        AttackSeconds = 1.7, DecaySeconds = 0.05, SustainLevel = 1.0, ReleaseSeconds = 0.12,
        Gain = 0.75
    };

    public static FmSynthInstrument GlassBells()
    {
        var fm = new FmSynthInstrument();
        fm.LoadPreset(1);
        return fm;
    }

    public static FmSynthInstrument WarmPad()
    {
        var fm = new FmSynthInstrument();
        fm.LoadPreset(2);
        return fm;
    }

    public static FmSynthInstrument ElectricPiano()
    {
        var fm = new FmSynthInstrument();
        fm.LoadPreset(3);
        return fm;
    }

    public static FmSynthInstrument MetallicHit()
    {
        var fm = new FmSynthInstrument();
        fm.LoadPreset(4);
        return fm;
    }

    public static FmSynthInstrument BassGrowlFm()
    {
        var fm = new FmSynthInstrument();
        fm.LoadPreset(5);
        return fm;
    }

    public static FmSynthInstrument CrystalPluckFm()
    {
        var fm = new FmSynthInstrument();
        fm.LoadPreset(6);
        return fm;
    }

    public static FmSynthInstrument SoftClarinet()
    {
        var fm = new FmSynthInstrument();
        fm.LoadPreset(7);
        return fm;
    }

    public static FmSynthInstrument BrightStabFm()
    {
        var fm = new FmSynthInstrument();
        fm.LoadPreset(8);
        return fm;
    }

    public static TripleOscInstrument WhiteRiser() => new()
    {
        Wave1 = (int)OscWave.Noise,
        Wave2 = (int)OscWave.Saw, Fine2 = 18, Level2 = 0.18,
        Wave3 = (int)OscWave.Saw, Fine3 = -18, Level3 = 0.18,
        FilterTypeIndex = 1, Cutoff = 800, Resonance = 2.0,
        AttackSeconds = 2.5, DecaySeconds = 1.0, SustainLevel = 1.0, ReleaseSeconds = 0.4,
        Gain = 0.7
    };

    public static TripleOscInstrument PluckStack() => new()
    {
        Wave1 = (int)OscWave.Saw,
        Wave2 = (int)OscWave.Square, Coarse2 = 12, Level2 = 0.28,
        Wave3 = (int)OscWave.Saw, Level3 = 0.0,
        FilterTypeIndex = 1, Cutoff = 4200, Resonance = 1.2,
        AttackSeconds = 0.001, DecaySeconds = 0.22, SustainLevel = 0.0, ReleaseSeconds = 0.18,
        Gain = 0.78
    };

    public static TripleOscInstrument SuperSawLead() => new()
    {
        Wave1 = (int)OscWave.Saw,
        Wave2 = (int)OscWave.Saw, Fine2 = 12, Level2 = 0.7,
        Wave3 = (int)OscWave.Saw, Fine3 = -12, Level3 = 0.7,
        FilterTypeIndex = 1, Cutoff = 6500, Resonance = 1.4,
        AttackSeconds = 0.01, DecaySeconds = 0.25, SustainLevel = 0.7, ReleaseSeconds = 0.3,
        Gain = 0.72
    };

    public static TripleOscInstrument ReeseGrowl() => new()
    {
        Wave1 = (int)OscWave.Saw,
        Wave2 = (int)OscWave.Saw, Fine2 = 18, Level2 = 0.9,
        Wave3 = (int)OscWave.Square, Coarse3 = -12, Level3 = 0.4,
        FilterTypeIndex = 1, Cutoff = 700, Resonance = 3.5,
        AttackSeconds = 0.02, DecaySeconds = 0.35, SustainLevel = 0.8, ReleaseSeconds = 0.25,
        Gain = 0.7
    };

    public static TripleOscInstrument AcidSquare() => new()
    {
        Wave1 = (int)OscWave.Square, Level2 = 0, Level3 = 0,
        FilterTypeIndex = 1, Cutoff = 600, Resonance = 8.0,
        AttackSeconds = 0.001, DecaySeconds = 0.2, SustainLevel = 0.15, ReleaseSeconds = 0.08,
        Gain = 0.8
    };

    public static TripleOscInstrument SoftKeys() => new()
    {
        Wave1 = (int)OscWave.Triangle,
        Wave2 = (int)OscWave.Sine, Coarse2 = 12, Level2 = 0.25,
        Level3 = 0,
        FilterTypeIndex = 1, Cutoff = 5000, Resonance = 0.6,
        AttackSeconds = 0.005, DecaySeconds = 0.8, SustainLevel = 0.2, ReleaseSeconds = 0.5,
        Gain = 0.75
    };

    public static TripleOscInstrument DetunedPad() => new()
    {
        Wave1 = (int)OscWave.Saw,
        Wave2 = (int)OscWave.Saw, Fine2 = 8, Level2 = 0.8,
        Wave3 = (int)OscWave.Triangle, Coarse3 = -12, Level3 = 0.35,
        FilterTypeIndex = 1, Cutoff = 3500, Resonance = 0.9,
        AttackSeconds = 0.6, DecaySeconds = 0.8, SustainLevel = 0.85, ReleaseSeconds = 1.8,
        Gain = 0.68
    };

    public static TripleOscInstrument HooverStab() => new()
    {
        Wave1 = (int)OscWave.Saw,
        Wave2 = (int)OscWave.Saw, Fine2 = 25, Level2 = 0.85,
        Wave3 = (int)OscWave.Saw, Fine3 = -20, Level3 = 0.85,
        FilterTypeIndex = 1, Cutoff = 2200, Resonance = 2.0,
        AttackSeconds = 0.001, DecaySeconds = 0.28, SustainLevel = 0.0, ReleaseSeconds = 0.2,
        Gain = 0.75
    };

    public static TripleOscInstrument OrganDrawbar() => new()
    {
        Wave1 = (int)OscWave.Sine,
        Wave2 = (int)OscWave.Sine, Coarse2 = 12, Level2 = 0.5,
        Wave3 = (int)OscWave.Sine, Coarse3 = 19, Level3 = 0.25,
        FilterTypeIndex = 1, Cutoff = 8000, Resonance = 0.5,
        AttackSeconds = 0.01, DecaySeconds = 0.1, SustainLevel = 0.9, ReleaseSeconds = 0.15,
        Gain = 0.7
    };

    private static OscillatorInstrument OscClassicSaw() => new()
    {
        Waveform = Waveform.Sawtooth,
        AttackSeconds = 0.005, DecaySeconds = 0.1, SustainLevel = 0.7, ReleaseSeconds = 0.25
    };

    private static OscillatorInstrument OscPureSine() => new()
    {
        Waveform = Waveform.Sine,
        AttackSeconds = 0.01, DecaySeconds = 0.05, SustainLevel = 0.9, ReleaseSeconds = 0.2
    };

    private static OscillatorInstrument OscPulseLead() => new()
    {
        Waveform = Waveform.Square,
        AttackSeconds = 0.002, DecaySeconds = 0.15, SustainLevel = 0.6, ReleaseSeconds = 0.18
    };

    private static OscillatorInstrument OscSoftPad() => new()
    {
        Waveform = Waveform.Sawtooth,
        AttackSeconds = 0.5, DecaySeconds = 0.4, SustainLevel = 0.85, ReleaseSeconds = 1.5
    };

    private static OscillatorInstrument OscPluck() => new()
    {
        Waveform = Waveform.Sawtooth,
        AttackSeconds = 0.001, DecaySeconds = 0.25, SustainLevel = 0.0, ReleaseSeconds = 0.2
    };

    private static OscillatorInstrument OscOrganTone() => new()
    {
        Waveform = Waveform.Sine,
        AttackSeconds = 0.02, DecaySeconds = 0.05, SustainLevel = 1.0, ReleaseSeconds = 0.1
    };

    private static OscillatorInstrument OscBassSine() => new()
    {
        Waveform = Waveform.Sine,
        AttackSeconds = 0.005, DecaySeconds = 0.15, SustainLevel = 0.7, ReleaseSeconds = 0.12
    };

    private static OscillatorInstrument OscSquareKeys() => new()
    {
        Waveform = Waveform.Square,
        AttackSeconds = 0.003, DecaySeconds = 0.6, SustainLevel = 0.25, ReleaseSeconds = 0.4
    };

    private static WavetableInstrument WtBasicInit() => new();

    private static WavetableInstrument WtMovingPad() => new()
    {
        Position = 0.3, UnisonVoices = 5, DetuneCents = 18, Spread = 0.8,
        Cutoff = 6000, Resonance = 0.8,
        AttackSeconds = 0.5, DecaySeconds = 0.6, SustainLevel = 0.85, ReleaseSeconds = 1.6,
        LfoRate = 0.25, LfoDepth = 0.35, Level = 0.7
    };

    private static WavetableInstrument WtGrittyBass() => new()
    {
        Position = 0.6, Warp = 0, Shape = 0.4, UnisonVoices = 3, DetuneCents = 10,
        FilterType = 0, Cutoff = 900, Resonance = 2.5,
        AttackSeconds = 0.01, DecaySeconds = 0.2, SustainLevel = 0.7, ReleaseSeconds = 0.15,
        Level = 0.8
    };

    private static WavetableInstrument WtGlassLead() => new()
    {
        Position = 0.8, UnisonVoices = 1, Cutoff = 10000, Resonance = 0.6,
        AttackSeconds = 0.005, DecaySeconds = 0.3, SustainLevel = 0.5, ReleaseSeconds = 0.35,
        LfoRate = 4.0, LfoDepth = 0.1, Level = 0.75
    };

    private static WavetableInstrument WtWideUnison() => new()
    {
        Position = 0.4, UnisonVoices = 7, DetuneCents = 22, Spread = 1.0,
        Cutoff = 8000, AttackSeconds = 0.02, DecaySeconds = 0.25, SustainLevel = 0.75,
        ReleaseSeconds = 0.4, Level = 0.65
    };

    private static WavetableInstrument WtPluckMorph() => new()
    {
        Position = 0.2, Shape = 0.3, UnisonVoices = 2,
        Cutoff = 5000, Resonance = 1.2,
        AttackSeconds = 0.001, DecaySeconds = 0.3, SustainLevel = 0.0, ReleaseSeconds = 0.25,
        LfoRate = 0.5, LfoDepth = 0.2, Level = 0.78
    };

    private static WavetableInstrument WtDarkSweep() => new()
    {
        Position = 0.1, UnisonVoices = 3, DetuneCents = 12,
        Cutoff = 800, Resonance = 3.0,
        AttackSeconds = 0.1, DecaySeconds = 0.5, SustainLevel = 0.6, ReleaseSeconds = 0.5,
        LfoRate = 0.15, LfoDepth = 0.5, Level = 0.7
    };

    private static WavetableInstrument WtAirKeys() => new()
    {
        Position = 0.5, UnisonVoices = 1, FilterType = 1, Cutoff = 200, Resonance = 0.5,
        AttackSeconds = 0.008, DecaySeconds = 1.0, SustainLevel = 0.2, ReleaseSeconds = 0.8,
        Level = 0.72
    };

    private static GranularInstrument GranTextureCloud() => new()
    {
        GrainSizeMs = 90, DensityHz = 30, Spray = 0.15, PanSpread = 0.6,
        AttackSeconds = 0.3, ReleaseSeconds = 1.0, Gain = 0.7
    };

    private static GranularInstrument GranStretchedPad() => new()
    {
        GrainSizeMs = 150, DensityHz = 18, Spray = 0.05, ScanSpeed = 0.02,
        PitchRandomSemitones = 0.2, AttackSeconds = 0.8, ReleaseSeconds = 2.0, Gain = 0.65
    };

    private static GranularInstrument GranGlitchSpray() => new()
    {
        GrainSizeMs = 25, DensityHz = 48, Spray = 0.4, PitchRandomSemitones = 4,
        Direction = 3, AttackSeconds = 0.01, ReleaseSeconds = 0.3, Gain = 0.7
    };

    private static GranularInstrument GranReverseHaze() => new()
    {
        GrainSizeMs = 120, DensityHz = 20, Direction = 1, Spray = 0.1,
        AttackSeconds = 0.5, ReleaseSeconds = 1.5, Gain = 0.6
    };

    private static GranularInstrument GranDenseSwarm() => new()
    {
        GrainSizeMs = 60, DensityHz = 60, Streams = 3, StreamSpread = 0.3, PanSpread = 0.8,
        AttackSeconds = 0.2, ReleaseSeconds = 0.8, Gain = 0.55
    };

    private static GranularInstrument GranSparseDrops() => new()
    {
        GrainSizeMs = 80, DensityHz = 4, Spray = 0.25, PanSpread = 1.0,
        AttackSeconds = 0.05, ReleaseSeconds = 1.2, Gain = 0.75
    };

    private static GranularInstrument GranPitchWash() => new()
    {
        GrainSizeMs = 100, DensityHz = 22, PitchRandomSemitones = 7, Spray = 0.2,
        AttackSeconds = 0.4, ReleaseSeconds = 1.8, Gain = 0.6
    };

    private static GranularInstrument GranLoopScrape() => new()
    {
        GrainSizeMs = 40, DensityHz = 35, ScanSpeed = 0.15, Spray = 0.08,
        AttackSeconds = 0.02, ReleaseSeconds = 0.4, Gain = 0.72
    };

    private static BasicSamplerInstrument SamplerTight() => new()
        { AttackSeconds = 0.001, ReleaseSeconds = 0.05, Gain = 0.95 };

    private static BasicSamplerInstrument SamplerPadSoft() => new()
        { AttackSeconds = 0.4, ReleaseSeconds = 1.2, Gain = 0.75 };

    private static BasicSamplerInstrument SamplerPluck() => new()
        { AttackSeconds = 0.001, ReleaseSeconds = 0.25, Gain = 0.9 };

    private static BasicSamplerInstrument SamplerSustain() => new()
        { AttackSeconds = 0.05, ReleaseSeconds = 0.4, Gain = 0.85 };

    private static BasicSamplerInstrument SamplerLongTail() => new()
        { AttackSeconds = 0.01, ReleaseSeconds = 2.5, Gain = 0.8 };

    private static BasicSamplerInstrument SamplerPunch() => new()
        { AttackSeconds = 0.001, ReleaseSeconds = 0.08, Gain = 1.0 };

    private static BasicSamplerInstrument SamplerAmbient() => new()
        { AttackSeconds = 0.8, ReleaseSeconds = 3.0, Gain = 0.65 };

    private static BasicSamplerInstrument SamplerSnap() => new()
        { AttackSeconds = 0.001, ReleaseSeconds = 0.03, Gain = 0.92 };

    private static BasicSamplerInstrument SamplerPluckedString() => new()
    {
        VoiceMode = 1,
        Damping = 0.55,
        PickPosition = 0.35,
        Brightness = 0.6,
        AttackSeconds = 0.001,
        ReleaseSeconds = 0.4,
        Gain = 0.85
    };

    private static BassSynthInstrument BassFromPreset(int index)
    {
        var inst = new BassSynthInstrument();
        inst.LoadPreset(index);
        return inst;
    }

    public static BassSynthInstrument BassDeepSub() => BassFromPreset(1);
    public static BassSynthInstrument BassReese() => BassFromPreset(2);
    public static BassSynthInstrument BassAcidPulse() => BassFromPreset(3);
    public static BassSynthInstrument BassWarmSquare() => BassFromPreset(4);
    public static BassSynthInstrument BassPlucky() => BassFromPreset(5);
    public static BassSynthInstrument BassGrowlDrive() => BassFromPreset(6);
    public static BassSynthInstrument BassSoftSine() => BassFromPreset(7);
    public static BassSynthInstrument BassFunkySlap() => BassFromPreset(8);

    // ---- Organ ----

    private static OrganInstrument OrganClassic888()
    {
        var o = new OrganInstrument();
        o.Drawbars[0] = 8; o.Drawbars[1] = 0; o.Drawbars[2] = 8; o.Drawbars[3] = 8; o.Drawbars[4] = 0;
        o.Drawbars[5] = 8; o.Drawbars[6] = 0; o.Drawbars[7] = 0; o.Drawbars[8] = 8;
        return o;
    }

    private static OrganInstrument OrganJazzDrawbars()
    {
        var o = new OrganInstrument();
        o.Drawbars[0] = 4; o.Drawbars[1] = 0; o.Drawbars[2] = 6; o.Drawbars[3] = 5; o.Drawbars[4] = 3;
        o.Drawbars[5] = 4; o.Drawbars[6] = 0; o.Drawbars[7] = 2; o.Drawbars[8] = 3;
        o.VibratoOn = true; o.VibratoRate = 4.5; o.VibratoDepth = 22;
        return o;
    }

    private static OrganInstrument OrganGospelFull()
    {
        var o = new OrganInstrument();
        o.Drawbars[0] = 6; o.Drawbars[1] = 4; o.Drawbars[2] = 8; o.Drawbars[3] = 7; o.Drawbars[4] = 5;
        o.Drawbars[5] = 6; o.Drawbars[6] = 4; o.Drawbars[7] = 3; o.Drawbars[8] = 5;
        o.PercussionOn = true; o.PercussionLevel = 0.7;
        return o;
    }

    private static OrganInstrument OrganRockB3()
    {
        var o = new OrganInstrument();
        o.Drawbars[0] = 8; o.Drawbars[1] = 6; o.Drawbars[2] = 8; o.Drawbars[3] = 6; o.Drawbars[4] = 0;
        o.Drawbars[5] = 8; o.Drawbars[6] = 0; o.Drawbars[7] = 0; o.Drawbars[8] = 6;
        o.PercussionOn = true; o.PercussionLevel = 0.85; o.PercussionDecayMs = 60;
        o.VibratoOn = false;
        return o;
    }

    private static OrganInstrument OrganSoftCathedral()
    {
        var o = new OrganInstrument();
        o.Drawbars[0] = 8; o.Drawbars[1] = 6; o.Drawbars[2] = 6; o.Drawbars[3] = 4; o.Drawbars[4] = 0;
        o.Drawbars[5] = 3; o.Drawbars[6] = 0; o.Drawbars[7] = 2; o.Drawbars[8] = 2;
        o.ReleaseSeconds = 0.25;
        return o;
    }

    private static OrganInstrument OrganPercussiveStab()
    {
        var o = new OrganInstrument();
        o.Drawbars[2] = 8; o.Drawbars[3] = 6; o.Drawbars[5] = 4;
        o.PercussionOn = true; o.PercussionLevel = 0.95; o.PercussionDecayMs = 45;
        o.AttackSeconds = 0.001; o.ReleaseSeconds = 0.05;
        return o;
    }

    private static OrganInstrument OrganVibratoWarm()
    {
        var o = new OrganInstrument();
        o.Drawbars[0] = 5; o.Drawbars[2] = 7; o.Drawbars[3] = 5; o.Drawbars[5] = 4;
        o.VibratoOn = true; o.VibratoRate = 5.0; o.VibratoDepth = 40;
        return o;
    }

    private static OrganInstrument OrganMinimalSub()
    {
        var o = new OrganInstrument();
        o.Drawbars[0] = 8; o.Drawbars[2] = 3;
        o.VibratoOn = false; o.PercussionOn = false;
        return o;
    }

    // ---- Phase-4 ----

    private static Phase4Instrument Phase4FmBell() => new()
    {
        Ratio1 = 1, Amount1 = 0.15, Ratio2 = 3.5, Amount2 = 0.7, Ratio3 = 5, Amount3 = 0.35, Ratio4 = 7, Amount4 = 0.2,
        FilterOn = true, Cutoff = 12000, AttackSeconds = 0.002, DecaySeconds = 1.2, SustainLevel = 0.1, ReleaseSeconds = 0.8
    };

    private static Phase4Instrument Phase4DigitalPad() => new()
    {
        Ratio1 = 1, Amount1 = 0.35, Ratio2 = 2, Amount2 = 0.5, Ratio3 = 3, Amount3 = 0.4, Ratio4 = 4, Amount4 = 0.25,
        FilterOn = true, Cutoff = 4500, AttackSeconds = 0.15, DecaySeconds = 0.6, SustainLevel = 0.75, ReleaseSeconds = 1.0
    };

    private static Phase4Instrument Phase4ResoLead() => new()
    {
        Ratio1 = 1, Amount1 = 0.55, Ratio2 = 2, Amount2 = 0.65, Ratio3 = 3, Amount3 = 0.45, Ratio4 = 4, Amount4 = 0.3,
        FilterOn = true, Cutoff = 3200, Resonance = 4.5,
        AttackSeconds = 0.004, DecaySeconds = 0.18, SustainLevel = 0.55, ReleaseSeconds = 0.2
    };

    private static Phase4Instrument Phase4HarshBass() => new()
    {
        Ratio1 = 0.5, Amount1 = 0.8, Ratio2 = 1, Amount2 = 0.7, Ratio3 = 2, Amount3 = 0.5, Ratio4 = 3, Amount4 = 0.35,
        FilterOn = true, Cutoff = 900, Resonance = 2.0,
        AttackSeconds = 0.003, DecaySeconds = 0.25, SustainLevel = 0.7, ReleaseSeconds = 0.12
    };

    private static Phase4Instrument Phase4GlassPluck() => new()
    {
        Ratio1 = 1, Amount1 = 0.25, Ratio2 = 2.5, Amount2 = 0.75, Ratio3 = 4, Amount3 = 0.55, Ratio4 = 6, Amount4 = 0.3,
        FilterOn = true, Cutoff = 7000,
        AttackSeconds = 0.001, DecaySeconds = 0.35, SustainLevel = 0.0, ReleaseSeconds = 0.3
    };

    private static Phase4Instrument Phase4WarmStack() => new()
    {
        Ratio1 = 1, Amount1 = 0.4, Ratio2 = 1.5, Amount2 = 0.55, Ratio3 = 2, Amount3 = 0.5, Ratio4 = 2.5, Amount4 = 0.4,
        FilterOn = true, Cutoff = 5500, Resonance = 1.2,
        AttackSeconds = 0.01, DecaySeconds = 0.4, SustainLevel = 0.65, ReleaseSeconds = 0.35
    };

    private static Phase4Instrument Phase4MetallicKeys() => new()
    {
        Ratio1 = 1, Amount1 = 0.3, Ratio2 = 2.75, Amount2 = 0.85, Ratio3 = 4.5, Amount3 = 0.6, Ratio4 = 6, Amount4 = 0.4,
        FilterOn = false,
        AttackSeconds = 0.001, DecaySeconds = 0.5, SustainLevel = 0.15, ReleaseSeconds = 0.4
    };

    private static Phase4Instrument Phase4CzBrass() => new()
    {
        Ratio1 = 1, Amount1 = 0.5, Ratio2 = 2, Amount2 = 0.8, Ratio3 = 3, Amount3 = 0.55, Ratio4 = 4, Amount4 = 0.35,
        FilterOn = true, Cutoff = 2800, Resonance = 3.0,
        AttackSeconds = 0.008, DecaySeconds = 0.22, SustainLevel = 0.6, ReleaseSeconds = 0.18
    };

    // ---- Drum Model ----

    private static DrumModelInstrument Drum808Kick()
    {
        var d = new DrumModelInstrument();
        d.ApplyModel(26); // v9 Kick
        d.Pitch = 0.35; d.Decay = 0.72; d.Tone = 0.4; d.Drive = 0.55; d.Gain = 0.9;
        return d;
    }

    private static DrumModelInstrument Drum909Snare()
    {
        var d = new DrumModelInstrument();
        d.ApplyModel(29); // v9 Snare
        d.Pitch = 0.55; d.Decay = 0.45; d.Noise = 0.7; d.Drive = 0.4; d.Gain = 0.88;
        return d;
    }

    private static DrumModelInstrument DrumTightHat()
    {
        var d = new DrumModelInstrument();
        d.ApplyModel(24); // v9 Hat Closed
        d.Decay = 0.25; d.Tone = 0.65; d.Noise = 0.8; d.Gain = 0.82;
        return d;
    }

    private static DrumModelInstrument DrumRoomClap()
    {
        var d = new DrumModelInstrument();
        d.ApplyModel(22); // v9 Clap
        d.Decay = 0.6; d.Noise = 0.75; d.Drive = 0.35; d.Gain = 0.85;
        return d;
    }

    private static DrumModelInstrument DrumTechnoZap()
    {
        var d = new DrumModelInstrument();
        d.ApplyModel(5); // v0 Zap Kick
        d.Pitch = 0.8; d.Decay = 0.35; d.Drive = 0.75; d.Gain = 0.92;
        return d;
    }

    private static DrumModelInstrument DrumLoFiTom()
    {
        var d = new DrumModelInstrument();
        d.ApplyModel(30); // v9 Tom
        d.Pitch = 0.4; d.Decay = 0.55; d.Tone = 0.35; d.Drive = 0.5; d.Gain = 0.8;
        return d;
    }

    private static DrumModelInstrument DrumVintageCymbal()
    {
        var d = new DrumModelInstrument();
        d.ApplyModel(0);
        d.Decay = 0.85; d.Tone = 0.7; d.Noise = 0.65; d.Gain = 0.75;
        return d;
    }

    private static DrumModelInstrument DrumPunchRim()
    {
        var d = new DrumModelInstrument();
        d.ApplyModel(28); // v9 Rimshot
        d.Pitch = 0.6; d.Decay = 0.3; d.Tone = 0.8; d.Drive = 0.45; d.Gain = 0.9;
        return d;
    }

    // ---- Polysynth ----

    private static PolysynthInstrument PolysynthInit() => new();

    private static PolysynthInstrument PolysynthBrightLead() => new()
    {
        BlendOp = 0, OscMix = 0.35, Cutoff = 6200, Resonance = 1.8, FilterEnvAmt = 0.45,
        Attack = 0.002, Decay = 0.08, Sustain = 0.45, Release = 0.15,
        FAttack = 0.001, FDecay = 0.12, FSustain = 0.1, FRelease = 0.12
    };

    private static PolysynthInstrument PolysynthWarmPad() => new()
    {
        BlendOp = 0, OscMix = 0.55, Noise = 0.08, Cutoff = 1800, Resonance = 0.9, FilterEnvAmt = 0.25,
        Attack = 0.04, Decay = 0.35, Sustain = 0.82, Release = 0.6,
        FAttack = 0.02, FDecay = 0.5, FSustain = 0.35, FRelease = 0.45
    };

    private static PolysynthInstrument PolysynthAcidBass() => new()
    {
        BlendOp = 4, OscMix = 0.6, FilterMode = 0, Cutoff = 900, Resonance = 3.5, FilterEnvAmt = 0.7,
        Attack = 0.001, Decay = 0.05, Sustain = 0.2, Release = 0.08,
        FAttack = 0.001, FDecay = 0.08, FSustain = 0, FRelease = 0.06
    };

    private static PolysynthInstrument PolysynthPluck() => new()
    {
        BlendOp = 2, OscMix = 0.5, Cutoff = 4200, Resonance = 1.4, FilterEnvAmt = 0.55,
        Attack = 0.001, Decay = 0.18, Sustain = 0, Release = 0.12,
        FAttack = 0.001, FDecay = 0.22, FSustain = 0, FRelease = 0.15
    };

    private static PolysynthInstrument PolysynthNoiseSweep() => new()
    {
        BlendOp = 1, OscMix = 0.45, Noise = 0.35, FilterFm = 0.4, Cutoff = 2800, Resonance = 2.2,
        Attack = 0.01, Decay = 0.4, Sustain = 0.5, Release = 0.35
    };

    // ---- Polymer ----

    private static PolymerInstrument PolymerInit() => new();

    private static PolymerInstrument PolymerGlassPad() => new()
    {
        WaveA = 1, WaveB = 2, FineB = 7, LevelA = 0.55, LevelB = 0.45,
        Cutoff = 1400, Resonance = 0.8, LfoRate = 0.18, LfoDepth = 0.15,
        Attack = 0.03, Decay = 0.4, Sustain = 0.85, Release = 0.7
    };

    private static PolymerInstrument PolymerDetunedStack() => new()
    {
        WaveA = 2, WaveB = 2, FineB = 14, LevelA = 0.6, LevelB = 0.6,
        Cutoff = 3200, Resonance = 1.1, FilterEnvAmt = 0.2, LfoRate = 0.5, LfoDepth = 0.35,
        Attack = 0.008, Decay = 0.25, Sustain = 0.7, Release = 0.3
    };

    private static PolymerInstrument PolymerSoftKeys() => new()
    {
        WaveA = 0, WaveB = 1, FineB = -5, LevelA = 0.7, LevelB = 0.35,
        Cutoff = 4500, Resonance = 0.6, FilterEnvAmt = 0.08,
        Attack = 0.003, Decay = 0.15, Sustain = 0.55, Release = 0.2
    };

    private static PolymerInstrument PolymerFilterWobble() => new()
    {
        WaveA = 2, WaveB = 3, FineB = -12, Cutoff = 900, Resonance = 2.4, FilterEnvAmt = 0.35,
        LfoRate = 2.5, LfoDepth = 0.65,
        Attack = 0.002, Decay = 0.12, Sustain = 0.6, Release = 0.18
    };

    // ---- New FX ----

    private static ConvolutionEffect ConvolutionSmallRoom() => new()
        { DecaySeconds = 0.45, Mix = 0.22, Size = 0.35 };

    private static ConvolutionEffect ConvolutionHallWash() => new()
        { DecaySeconds = 2.2, Mix = 0.38, Size = 0.85 };

    private static ConvolutionEffect ConvolutionPlateBright() => new()
        { DecaySeconds = 1.1, Mix = 0.3, Size = 0.55 };

    private static RotaryEffect RotarySlowLeslie() => new()
        { SpeedIndex = 0, SpeedHz = 0.8, DriveDb = 3, Mix = 0.85 };

    private static RotaryEffect RotaryFastSpin() => new()
        { SpeedIndex = 1, SpeedHz = 6.5, DriveDb = 6, Mix = 1.0 };

    private static RotaryEffect RotaryDriveWarm() => new()
        { SpeedIndex = 0, SpeedHz = 1.2, DriveDb = 12, Mix = 0.75 };

    private static DynamicsEffect DynamicsVocalComp() => new()
        { ThresholdDb = -20, Ratio = 3.5, AttackMs = 12, ReleaseMs = 100, MakeupDb = 3 };

    private static DynamicsEffect DynamicsDrumPunch() => new()
        { ThresholdDb = -16, Ratio = 5.0, AttackMs = 6, ReleaseMs = 80, MakeupDb = 4 };

    private static DynamicsEffect DynamicsExpandGate() => new()
        { ThresholdDb = -32, Ratio = 2.5, AttackMs = 5, ReleaseMs = 200, MakeupDb = 2 };

    private static DelayPlusEffect DelayPlusDubEcho() => new()
        { TimeMs = 480, Feedback = 0.55, Mix = 0.42, ModRateHz = 0.25, ModDepthMs = 6, FilterHz = 3200 };

    private static DelayPlusEffect DelayPlusModulatedPing() => new()
        { TimeMs = 320, Feedback = 0.38, Mix = 0.35, ModRateHz = 1.2, ModDepthMs = 10, FilterHz = 6000 };

    private static DelayPlusEffect DelayPlusFilteredSlap() => new()
        { TimeMs = 120, Feedback = 0.22, Mix = 0.28, ModRateHz = 0.5, ModDepthMs = 2, FilterHz = 4500 };
}
