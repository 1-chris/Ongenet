using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Padda — a lush pad synthesizer. Two unison oscillator layers (each a fat
/// <see cref="UnisonOscillator"/> stack) plus a sine sub and a noise source feed a resonant filter
/// shaped by its own envelope, an LFO and key tracking; an amp envelope and analog
/// <see cref="DriftGenerator"/> keep it breathing. The summed voices then pass through an internal
/// drive → chorus → delay → reverb chain for size and space. Ships an init patch plus five built-in
/// presets spanning deep grungy, angelic/massive, and environmental textures
/// (see <see cref="IPresetProvider"/>). A loaded sample becomes the "Custom" waveform
/// (<see cref="ISampleHost"/>). Built on the shared polyphonic instrument framework.
/// </summary>
public sealed class PaddaInstrument : PolyphonicInstrument, ISampleHost, IPresetProvider
{
    public const string TypeId = "padda";

    protected override string GetTypeId() => TypeId;

    private const int MaxUnison = 7;

    private Parameter[]? _parameters;
    private volatile float[]? _customTable;
    private volatile AudioSampleBuffer? _loadedSample; // retained so a project save can embed it

    // Internal post-mix effect chain (reused engine effects), plus a scratch mix buffer.
    private readonly ChorusEffect _chorus = new();
    private readonly DelayEffect _delay = new();
    private readonly ReverbEffect _reverb = new();
    private float[] _scratch = Array.Empty<float>();

    // The most recently started note, so glide can slur from the previous pitch.
    internal int LastNote = -1;

    public PaddaInstrument() : base(polyphony: 8) => Reset();

    public override string Name => "Padda";

    // --- Layer A ---
    public int WaveA { get; set; }
    public int OctaveA { get; set; }
    public double CoarseA { get; set; }
    public double LevelA { get; set; }

    // --- Layer B ---
    public int WaveB { get; set; }
    public int OctaveB { get; set; }
    public double CoarseB { get; set; }
    public double LevelB { get; set; }

    // --- Unison (shared by both layers) ---
    public int UnisonVoices { get; set; }
    public double UnisonDetune { get; set; } // cents
    public double UnisonWidth { get; set; }  // 0..1
    public double UnisonBlend { get; set; }  // detuned-voice level vs centre

    // --- Sub / Noise ---
    public double SubLevel { get; set; }
    public double NoiseLevel { get; set; }

    // --- Filter ---
    public int FilterType { get; set; }       // 0 LP, 1 HP, 2 BP
    public double Cutoff { get; set; }
    public double Resonance { get; set; }
    public double FilterEnvAmount { get; set; } // -1..1 (octaves = ±4)
    public double KeyTrack { get; set; }        // 0..1

    // --- Filter envelope ---
    public double FAttack { get; set; }
    public double FDecay { get; set; }
    public double FSustain { get; set; }
    public double FRelease { get; set; }

    // --- Amp envelope ---
    public double Attack { get; set; }
    public double Decay { get; set; }
    public double Sustain { get; set; }
    public double Release { get; set; }

    // --- LFO ---
    public double LfoRate { get; set; }
    public int LfoShape { get; set; }
    public double LfoDepth { get; set; }
    public int LfoDest { get; set; } // 0 Off, 1 Cutoff, 2 Pitch, 3 Pan, 4 Volume

    // --- Movement / tone ---
    public double DriftAmount { get; set; }
    public double Drive { get; set; }

    // --- Chorus ---
    public double ChorusMix { get; set; }
    public double ChorusRate { get; set; }
    public double ChorusDepth { get; set; }

    // --- Reverb ---
    public double ReverbMix { get; set; }
    public double ReverbSize { get; set; }
    public double ReverbDamp { get; set; }

    // --- Delay ---
    public double DelayMix { get; set; }
    public double DelayTime { get; set; } // ms
    public double DelayFeedback { get; set; }

    // --- Output ---
    public double Gain { get; set; }
    public double Glide { get; set; } // seconds (portamento)

    // --- ISampleHost: the loaded sample becomes the Custom waveform cycle ---
    public string? SampleName { get; private set; }
    public AudioSampleBuffer? CurrentSample => _loadedSample;
    internal float[]? CustomTable => _customTable;

    public void LoadSample(AudioSampleBuffer sample, string name)
    {
        _customTable = SampleMixdown.ToMono(sample);
        _loadedSample = sample;
        SampleName = name;
    }

    private static readonly string[] WaveNames = { "Sine", "Triangle", "Saw", "Square", "Noise", "Custom" };
    private static readonly string[] FilterNames = { "Low Pass", "High Pass", "Band Pass" };
    private static readonly string[] LfoShapes = { "Sine", "Triangle", "Saw", "Square" };
    private static readonly string[] LfoDests = { "Off", "Cutoff", "Pitch", "Pan", "Volume" };

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Wave", WaveNames, () => WaveA, i => WaveA = i) { Group = "Layer A" },
        new FloatParameter("Octave", -2, 2, () => OctaveA, v => OctaveA = (int)Math.Round(v), "0", "oct") { Group = "Layer A" },
        new FloatParameter("Coarse", -24, 24, () => CoarseA, v => CoarseA = Math.Round(v), "0", "st") { Group = "Layer A" },
        new FloatParameter("Level", 0, 1, () => LevelA, v => LevelA = v, "0.00") { Group = "Layer A" },

        new ChoiceParameter("Wave", WaveNames, () => WaveB, i => WaveB = i) { Group = "Layer B" },
        new FloatParameter("Octave", -2, 2, () => OctaveB, v => OctaveB = (int)Math.Round(v), "0", "oct") { Group = "Layer B" },
        new FloatParameter("Coarse", -24, 24, () => CoarseB, v => CoarseB = Math.Round(v), "0", "st") { Group = "Layer B" },
        new FloatParameter("Level", 0, 1, () => LevelB, v => LevelB = v, "0.00") { Group = "Layer B" },

        new FloatParameter("Voices", 1, MaxUnison, () => UnisonVoices, v => UnisonVoices = (int)Math.Round(v), "0") { Group = "Unison" },
        new FloatParameter("Detune", 0, 50, () => UnisonDetune, v => UnisonDetune = v, "0", "ct") { Group = "Unison" },
        new FloatParameter("Width", 0, 1, () => UnisonWidth, v => UnisonWidth = v, "0.00") { Group = "Unison" },
        new FloatParameter("Blend", 0, 1, () => UnisonBlend, v => UnisonBlend = v, "0.00") { Group = "Unison" },

        new FloatParameter("Sub", 0, 1, () => SubLevel, v => SubLevel = v, "0.00") { Group = "Sub / Noise" },
        new FloatParameter("Noise", 0, 1, () => NoiseLevel, v => NoiseLevel = v, "0.00") { Group = "Sub / Noise" },

        new ChoiceParameter("Type", FilterNames, () => FilterType, i => FilterType = i) { Group = "Filter" },
        new FloatParameter("Cutoff", 20, 20000, () => Cutoff, v => Cutoff = v, "0", "Hz", skew: 3.0) { Group = "Filter" },
        new FloatParameter("Reso", 0.5, 16, () => Resonance, v => Resonance = v, "0.0", "Q", skew: 2.0) { Group = "Filter" },
        new FloatParameter("Env Amt", -1, 1, () => FilterEnvAmount, v => FilterEnvAmount = v, "0.00") { Group = "Filter" },
        new FloatParameter("Key Track", 0, 1, () => KeyTrack, v => KeyTrack = v, "0.00") { Group = "Filter" },

        new FloatParameter("Attack", 0.001, 8, () => FAttack, v => FAttack = v, "0.000", "s", skew: 2.0) { Group = "Filter Envelope" },
        new FloatParameter("Decay", 0.001, 8, () => FDecay, v => FDecay = v, "0.000", "s", skew: 2.0) { Group = "Filter Envelope" },
        new FloatParameter("Sustain", 0, 1, () => FSustain, v => FSustain = v, "0.00") { Group = "Filter Envelope" },
        new FloatParameter("Release", 0.001, 8, () => FRelease, v => FRelease = v, "0.000", "s", skew: 2.0) { Group = "Filter Envelope" },

        new FloatParameter("Attack", 0.001, 8, () => Attack, v => Attack = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },
        new FloatParameter("Decay", 0.001, 8, () => Decay, v => Decay = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },
        new FloatParameter("Sustain", 0, 1, () => Sustain, v => Sustain = v, "0.00") { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 12, () => Release, v => Release = v, "0.000", "s", skew: 2.0) { Group = "Amp Envelope" },

        new FloatParameter("Rate", 0.01, 20, () => LfoRate, v => LfoRate = v, "0.00", "Hz", skew: 2.0) { Group = "LFO" },
        new ChoiceParameter("Shape", LfoShapes, () => LfoShape, i => LfoShape = i) { Group = "LFO" },
        new FloatParameter("Depth", 0, 1, () => LfoDepth, v => LfoDepth = v, "0.00") { Group = "LFO" },
        new ChoiceParameter("Dest", LfoDests, () => LfoDest, i => LfoDest = i) { Group = "LFO" },

        new FloatParameter("Drift", 0, 1, () => DriftAmount, v => DriftAmount = v, "0.00") { Group = "Movement" },
        new FloatParameter("Drive", 0, 1, () => Drive, v => Drive = v, "0.00") { Group = "Movement" },

        new FloatParameter("Mix", 0, 1, () => ChorusMix, v => ChorusMix = v, "0.00") { Group = "Chorus" },
        new FloatParameter("Rate", 0.05, 5, () => ChorusRate, v => ChorusRate = v, "0.00", "Hz", skew: 2.0) { Group = "Chorus" },
        new FloatParameter("Depth", 0, 1, () => ChorusDepth, v => ChorusDepth = v, "0.00") { Group = "Chorus" },

        new FloatParameter("Mix", 0, 1, () => DelayMix, v => DelayMix = v, "0.00") { Group = "Delay" },
        new FloatParameter("Time", 1, 2000, () => DelayTime, v => DelayTime = v, "0", "ms", skew: 2.0) { Group = "Delay" },
        new FloatParameter("Feedback", 0, 0.95, () => DelayFeedback, v => DelayFeedback = v, "0.00") { Group = "Delay" },

        new FloatParameter("Mix", 0, 1, () => ReverbMix, v => ReverbMix = v, "0.00") { Group = "Reverb" },
        new FloatParameter("Size", 0, 1, () => ReverbSize, v => ReverbSize = v, "0.00") { Group = "Reverb" },
        new FloatParameter("Damping", 0, 1, () => ReverbDamp, v => ReverbDamp = v, "0.00") { Group = "Reverb" },

        new FloatParameter("Gain", 0, 1, () => Gain, v => Gain = v, "0.00") { Group = "Output" },
        new FloatParameter("Glide", 0, 2, () => Glide, v => Glide = v, "0.000", "s", skew: 2.0) { Group = "Output" }
    };

    protected override Voice CreateVoice() => new PadVoice(this);

    public override void Prepare(AudioFormat format)
    {
        base.Prepare(format);
        _chorus.Prepare(format);
        _delay.Prepare(format);
        _reverb.Prepare(format);
        var cap = Math.Max(2048, format.SampleRate / 10) * Math.Max(1, format.Channels);
        if (_scratch.Length < cap) _scratch = new float[cap];
    }

    public override void Render(Span<float> buffer)
    {
        var n = buffer.Length;
        if (n == 0) return;
        if (_scratch.Length < n) _scratch = new float[n];

        var mix = _scratch.AsSpan(0, n);
        mix.Clear();
        RenderVoices(mix);

        var drive = (float)Drive;
        if (drive > 0.0001f)
        {
            var pre = 1f + drive * 6f;
            for (var i = 0; i < n; i++) mix[i] = AudioMath.SoftClip(mix[i] * pre);
        }

        if (ChorusMix > 0.001)
        {
            _chorus.Mix = ChorusMix; _chorus.RateHz = ChorusRate; _chorus.Depth = ChorusDepth;
            _chorus.Process(mix);
        }

        if (DelayMix > 0.001)
        {
            _delay.Mix = DelayMix; _delay.TimeMs = DelayTime; _delay.Feedback = DelayFeedback;
            _delay.Process(mix);
        }

        if (ReverbMix > 0.001)
        {
            _reverb.Mix = ReverbMix; _reverb.RoomSize = ReverbSize; _reverb.Damping = ReverbDamp;
            _reverb.Process(mix);
        }

        var g = (float)Gain;
        for (var i = 0; i < n; i++) buffer[i] += mix[i] * g;
    }

    public override IInstrument Clone()
    {
        var c = new PaddaInstrument();
        CopyStateTo(c);
        c._customTable = _customTable;
        c._loadedSample = _loadedSample;
        c.SampleName = SampleName;
        return c;
    }

    private void CopyStateTo(PaddaInstrument c)
    {
        c.WaveA = WaveA; c.OctaveA = OctaveA; c.CoarseA = CoarseA; c.LevelA = LevelA;
        c.WaveB = WaveB; c.OctaveB = OctaveB; c.CoarseB = CoarseB; c.LevelB = LevelB;
        c.UnisonVoices = UnisonVoices; c.UnisonDetune = UnisonDetune; c.UnisonWidth = UnisonWidth; c.UnisonBlend = UnisonBlend;
        c.SubLevel = SubLevel; c.NoiseLevel = NoiseLevel;
        c.FilterType = FilterType; c.Cutoff = Cutoff; c.Resonance = Resonance; c.FilterEnvAmount = FilterEnvAmount; c.KeyTrack = KeyTrack;
        c.FAttack = FAttack; c.FDecay = FDecay; c.FSustain = FSustain; c.FRelease = FRelease;
        c.Attack = Attack; c.Decay = Decay; c.Sustain = Sustain; c.Release = Release;
        c.LfoRate = LfoRate; c.LfoShape = LfoShape; c.LfoDepth = LfoDepth; c.LfoDest = LfoDest;
        c.DriftAmount = DriftAmount; c.Drive = Drive;
        c.ChorusMix = ChorusMix; c.ChorusRate = ChorusRate; c.ChorusDepth = ChorusDepth;
        c.ReverbMix = ReverbMix; c.ReverbSize = ReverbSize; c.ReverbDamp = ReverbDamp;
        c.DelayMix = DelayMix; c.DelayTime = DelayTime; c.DelayFeedback = DelayFeedback;
        c.Gain = Gain; c.Glide = Glide;
    }

    // ===== Presets =====

    private static readonly string[] PresetNamesList =
        { "Init", "Cathedral Choir", "Tar Pit", "Aurora Shimmer", "Biotope", "Velvet Strings" };

    public IReadOnlyList<string> PresetNames => PresetNamesList;

    public void LoadPreset(int index)
    {
        switch (index)
        {
            case 1: Cathedral(); break;
            case 2: TarPit(); break;
            case 3: Aurora(); break;
            case 4: Biotope(); break;
            case 5: VelvetStrings(); break;
            default: Reset(); break;
        }
    }

    /// <summary>The init patch — a clean, gently moving saw pad. All presets start from here.</summary>
    private void Reset()
    {
        WaveA = (int)OscWave.Saw; OctaveA = 0; CoarseA = 0; LevelA = 0.8;
        WaveB = (int)OscWave.Saw; OctaveB = 0; CoarseB = 0; LevelB = 0.0;
        UnisonVoices = 3; UnisonDetune = 14; UnisonWidth = 0.5; UnisonBlend = 0.7;
        SubLevel = 0.0; NoiseLevel = 0.0;
        FilterType = 0; Cutoff = 12000; Resonance = 0.7; FilterEnvAmount = 0.0; KeyTrack = 0.0;
        FAttack = 0.05; FDecay = 0.4; FSustain = 0.6; FRelease = 0.6;
        Attack = 0.5; Decay = 0.4; Sustain = 0.9; Release = 1.0;
        LfoRate = 0.4; LfoShape = (int)LfoWave.Sine; LfoDepth = 0.2; LfoDest = 0;
        DriftAmount = 0.15; Drive = 0.0;
        ChorusMix = 0.0; ChorusRate = 0.4; ChorusDepth = 0.5;
        ReverbMix = 0.2; ReverbSize = 0.7; ReverbDamp = 0.4;
        DelayMix = 0.0; DelayTime = 350; DelayFeedback = 0.3;
        Gain = 0.8; Glide = 0.0;
    }

    /// <summary>Angelic, massive, melodic — slow-swelling stacked saws in a huge bright hall.</summary>
    private void Cathedral()
    {
        Reset();
        WaveA = (int)OscWave.Saw; LevelA = 0.8;
        WaveB = (int)OscWave.Saw; OctaveB = 1; LevelB = 0.7;
        UnisonVoices = 7; UnisonDetune = 22; UnisonWidth = 0.9; UnisonBlend = 0.85;
        SubLevel = 0.15;
        Cutoff = 9000; Resonance = 0.8; FilterEnvAmount = 0.25;
        FAttack = 1.2; FDecay = 1.5; FSustain = 0.8; FRelease = 1.5;
        Attack = 0.9; Decay = 0.5; Sustain = 1.0; Release = 2.0;
        LfoRate = 0.25; LfoShape = (int)LfoWave.Sine; LfoDepth = 0.2; LfoDest = 1; // cutoff
        DriftAmount = 0.2;
        ChorusMix = 0.5; ChorusRate = 0.3; ChorusDepth = 0.6;
        ReverbMix = 0.45; ReverbSize = 0.9; ReverbDamp = 0.25;
        DelayMix = 0.18; DelayTime = 480; DelayFeedback = 0.35;
        Gain = 0.75;
    }

    /// <summary>Deep and grungy — sub-heavy detuned saws and a gritty square, driven and dark.</summary>
    private void TarPit()
    {
        Reset();
        WaveA = (int)OscWave.Saw; LevelA = 0.9;
        WaveB = (int)OscWave.Square; OctaveB = -1; LevelB = 0.7;
        UnisonVoices = 5; UnisonDetune = 30; UnisonWidth = 0.6; UnisonBlend = 0.9;
        SubLevel = 0.4; NoiseLevel = 0.12;
        Cutoff = 1200; Resonance = 4.0; FilterEnvAmount = 0.3; KeyTrack = 0.3;
        FAttack = 0.2; FDecay = 0.8; FSustain = 0.4; FRelease = 0.8;
        Attack = 0.3; Decay = 0.5; Sustain = 0.85; Release = 1.0;
        LfoRate = 0.15; LfoShape = (int)LfoWave.Triangle; LfoDepth = 0.4; LfoDest = 1; // cutoff
        DriftAmount = 0.35; Drive = 0.6;
        ChorusMix = 0.35; ChorusRate = 0.25; ChorusDepth = 0.7;
        ReverbMix = 0.3; ReverbSize = 0.8; ReverbDamp = 0.6;
        DelayMix = 0.25; DelayTime = 420; DelayFeedback = 0.45;
        Gain = 0.7;
    }

    /// <summary>Angelic and bright — shimmering octave-up triangle over wide saws, airy long tail.</summary>
    private void Aurora()
    {
        Reset();
        WaveA = (int)OscWave.Saw; LevelA = 0.7;
        WaveB = (int)OscWave.Triangle; OctaveB = 1; LevelB = 0.6;
        UnisonVoices = 7; UnisonDetune = 16; UnisonWidth = 1.0; UnisonBlend = 0.7;
        SubLevel = 0.1; NoiseLevel = 0.04;
        Cutoff = 16000; Resonance = 0.6; FilterEnvAmount = 0.15; KeyTrack = 0.2;
        FAttack = 0.8; FDecay = 1.0; FSustain = 0.9; FRelease = 1.2;
        Attack = 0.7; Decay = 0.4; Sustain = 1.0; Release = 1.8;
        LfoRate = 0.8; LfoShape = (int)LfoWave.Sine; LfoDepth = 0.25; LfoDest = 1; // cutoff
        DriftAmount = 0.2;
        ChorusMix = 0.55; ChorusRate = 0.6; ChorusDepth = 0.5;
        ReverbMix = 0.5; ReverbSize = 0.92; ReverbDamp = 0.15;
        DelayMix = 0.3; DelayTime = 500; DelayFeedback = 0.4;
        Gain = 0.72;
    }

    /// <summary>Environmental SFX — resonant filtered noise that evolves and wanders, big and distant.</summary>
    private void Biotope()
    {
        Reset();
        WaveA = (int)OscWave.Noise; LevelA = 0.5;
        WaveB = (int)OscWave.Triangle; OctaveB = 0; LevelB = 0.4;
        UnisonVoices = 4; UnisonDetune = 35; UnisonWidth = 1.0; UnisonBlend = 0.8;
        SubLevel = 0.2; NoiseLevel = 0.5;
        Cutoff = 2500; Resonance = 6.0; FilterEnvAmount = 0.5; KeyTrack = 0.1;
        FAttack = 1.5; FDecay = 2.0; FSustain = 0.5; FRelease = 2.0;
        Attack = 1.5; Decay = 1.0; Sustain = 0.8; Release = 2.5;
        LfoRate = 0.12; LfoShape = (int)LfoWave.Triangle; LfoDepth = 0.8; LfoDest = 1; // cutoff
        DriftAmount = 0.6; Drive = 0.2;
        ChorusMix = 0.4; ChorusRate = 0.2; ChorusDepth = 0.8;
        ReverbMix = 0.6; ReverbSize = 0.95; ReverbDamp = 0.3;
        DelayMix = 0.4; DelayTime = 650; DelayFeedback = 0.55;
        Gain = 0.65;
    }

    /// <summary>Warm, melodic strings — tight detuned saws with vibrato and a touch of portamento.</summary>
    private void VelvetStrings()
    {
        Reset();
        WaveA = (int)OscWave.Saw; LevelA = 0.8;
        WaveB = (int)OscWave.Saw; OctaveB = 0; LevelB = 0.6;
        UnisonVoices = 5; UnisonDetune = 12; UnisonWidth = 0.7; UnisonBlend = 0.75;
        SubLevel = 0.1;
        Cutoff = 6000; Resonance = 0.8; FilterEnvAmount = 0.2; KeyTrack = 0.25;
        FAttack = 0.4; FDecay = 0.8; FSustain = 0.7; FRelease = 0.8;
        Attack = 0.25; Decay = 0.4; Sustain = 0.9; Release = 0.9;
        LfoRate = 5.0; LfoShape = (int)LfoWave.Sine; LfoDepth = 0.15; LfoDest = 2; // pitch (vibrato)
        DriftAmount = 0.18;
        ChorusMix = 0.45; ChorusRate = 0.5; ChorusDepth = 0.5;
        ReverbMix = 0.35; ReverbSize = 0.8; ReverbDamp = 0.4;
        DelayMix = 0.12; DelayTime = 360; DelayFeedback = 0.3;
        Gain = 0.78; Glide = 0.08;
    }

    /// <summary>One sounding note: two unison layers + sub + noise → modulated filter → amp envelope.</summary>
    private sealed class PadVoice : Voice
    {
        private const float VoiceGain = 0.18f;
        private const int ControlBlock = 32; // samples between filter-coefficient / pitch recomputes

        private readonly PaddaInstrument _inst;
        private readonly UnisonOscillator _layerA = new(MaxUnison);
        private readonly UnisonOscillator _layerB = new(MaxUnison);
        private readonly WaveOscillator _sub = new();
        private readonly WaveOscillator _noise = new();
        private readonly AdsrEnvelope _amp = new();
        private readonly AdsrEnvelope _filtEnv = new();
        private readonly Lfo _lfo = new();
        private readonly DriftGenerator _driftA = new();
        private readonly DriftGenerator _driftB = new();

        private Biquad _filterL, _filterR;
        private float _velocity;
        private double _pitchSemi;   // current (glided) pitch in MIDI semitones
        private double _targetSemi;  // destination pitch
        private double _filtEnvVal;  // latest filter-envelope level
        private double _lfoVal;      // latest LFO value
        private static uint _seed = 1;

        public PadVoice(PaddaInstrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            var sr = format.SampleRate;
            var seed = _seed++ * 2654435761u + (uint)midiNote;

            _layerA.SetSampleRate(sr); _layerA.Seed(seed);
            _layerB.SetSampleRate(sr); _layerB.Seed(seed ^ 0x5BD1E995u);
            _sub.SetSampleRate(sr); _sub.Wave = OscWave.Sine; _sub.SeedNoise(seed); _sub.ResetPhase(0);
            _noise.SetSampleRate(sr); _noise.Wave = OscWave.Noise; _noise.SeedNoise(seed ^ 0xA5A5A5A5u);

            _amp.SetSampleRate(sr);
            _amp.AttackSeconds = _inst.Attack; _amp.DecaySeconds = _inst.Decay;
            _amp.SustainLevel = _inst.Sustain; _amp.ReleaseSeconds = _inst.Release;
            _amp.Gate();

            _filtEnv.SetSampleRate(sr);
            _filtEnv.AttackSeconds = _inst.FAttack; _filtEnv.DecaySeconds = _inst.FDecay;
            _filtEnv.SustainLevel = _inst.FSustain; _filtEnv.ReleaseSeconds = _inst.FRelease;
            _filtEnv.Gate();

            _lfo.Reset((seed & 0xFFFF) / 65535.0); // per-voice LFO phase for stereo/voice spread
            // Drift is advanced once per control block, so configure it at the control rate.
            var controlRate = sr / (double)ControlBlock;
            _driftA.Configure(0.35, controlRate, seed ^ 0x1234u);
            _driftB.Configure(0.27, controlRate, seed ^ 0x9876u);

            _filterL.Reset(); _filterR.Reset();
            _filtEnvVal = 0; _lfoVal = 0;

            // Glide: slur from the previously played note when enabled, else jump to pitch.
            _targetSemi = midiNote;
            _pitchSemi = _inst.Glide > 0.0005 && _inst.LastNote >= 0 ? _inst.LastNote : midiNote;
            _inst.LastNote = midiNote;
        }

        public override void Release()
        {
            _amp.Release();
            _filtEnv.Release();
        }

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var sr = Format.SampleRate;

            // Block-level setup (cheap, picks up live edits).
            var waveA = (OscWave)_inst.WaveA; var waveB = (OscWave)_inst.WaveB;
            _layerA.Wave = waveA; _layerA.CustomTable = _inst.CustomTable;
            _layerB.Wave = waveB; _layerB.CustomTable = _inst.CustomTable;
            _layerA.Configure(_inst.UnisonVoices, _inst.UnisonDetune, _inst.UnisonWidth, _inst.UnisonBlend);
            _layerB.Configure(_inst.UnisonVoices, _inst.UnisonDetune, _inst.UnisonWidth, _inst.UnisonBlend);
            _lfo.Wave = (LfoWave)_inst.LfoShape;
            _lfo.SetRate(_inst.LfoRate, sr);

            var la = (float)_inst.LevelA; var lb = (float)_inst.LevelB;
            var subLvl = (float)_inst.SubLevel; var noiseLvl = (float)_inst.NoiseLevel;
            var depth = _inst.LfoDepth; var dest = _inst.LfoDest;
            var drift = _inst.DriftAmount;
            var maxCut = sr * 0.45;
            var mode = _inst.FilterType switch
            {
                1 => FilterMode.HighPass,
                2 => FilterMode.BandPass,
                _ => FilterMode.LowPass
            };
            // Glide coefficient per control block (exponential approach).
            var blocksPerSec = (double)sr / ControlBlock;
            var glideCoeff = _inst.Glide > 0.0005
                ? 1.0 - Math.Exp(-1.0 / (_inst.Glide * blocksPerSec))
                : 1.0;

            var coeffs = BiquadCoefficients.Identity;
            float panLg = 1f, panRg = 1f, ampScale = 1f;

            void Recompute()
            {
                _pitchSemi += (_targetSemi - _pitchSemi) * glideCoeff;

                var lfoPitch = dest == 2 ? _lfoVal * depth * 2.0 : 0.0;   // ±2 semitones
                var dA = _driftA.Next() * drift * 0.12;                    // ~±12 cents at full drift
                var dB = _driftB.Next() * drift * 0.12;
                var baseSemi = _pitchSemi + lfoPitch;

                _layerA.SetBaseFrequency(MusicalMath.NoteToFrequency(baseSemi + _inst.OctaveA * 12 + _inst.CoarseA + dA));
                _layerB.SetBaseFrequency(MusicalMath.NoteToFrequency(baseSemi + _inst.OctaveB * 12 + _inst.CoarseB + dB));
                _sub.SetFrequency(MusicalMath.NoteToFrequency(baseSemi - 12));

                var lfoCut = dest == 1 ? _lfoVal * depth * 2.0 : 0.0;      // ±2 octaves
                var keyOct = _inst.KeyTrack * (Note - 60) / 12.0;
                var octaves = _inst.FilterEnvAmount * 4.0 * _filtEnvVal + lfoCut + keyOct;
                var cutoff = AudioMath.Clamp(_inst.Cutoff * Math.Pow(2.0, octaves), 20.0, maxCut);
                coeffs = BiquadCoefficients.Compute(mode, cutoff, _inst.Resonance, sr);

                var panMod = dest == 3 ? _lfoVal * depth : 0.0;           // -1..1 balance
                panLg = panMod <= 0 ? 1f : (float)(1.0 - panMod);
                panRg = panMod >= 0 ? 1f : (float)(1.0 + panMod);
                ampScale = dest == 4 ? (float)(1.0 + _lfoVal * depth * 0.5) : 1f; // tremolo
            }

            Recompute();

            for (var frame = 0; frame < frames; frame++)
            {
                if ((frame & (ControlBlock - 1)) == 0 && frame != 0) Recompute();

                _layerA.Render(out var aL, out var aR);
                _layerB.Render(out var bL, out var bR);
                var centre = (subLvl > 0 ? _sub.Next() * subLvl : 0f) +
                             (noiseLvl > 0 ? _noise.Next() * noiseLvl : 0f);

                var l = aL * la + bL * lb + centre;
                var r = aR * la + bR * lb + centre;

                var e = _amp.Process();
                _filtEnvVal = _filtEnv.Process();
                _lfoVal = _lfo.Next();

                var amp = e * _velocity * VoiceGain * ampScale;
                l *= amp; r *= amp;
                l = (float)_filterL.Process(coeffs, l);
                r = (float)_filterR.Process(coeffs, r);
                l *= panLg; r *= panRg;

                var baseIndex = frame * channels;
                if (channels == 1)
                {
                    buffer[baseIndex] += (l + r) * 0.5f;
                }
                else
                {
                    buffer[baseIndex] += l;
                    buffer[baseIndex + 1] += r;
                    var extra = (l + r) * 0.5f;
                    for (var ch = 2; ch < channels; ch++) buffer[baseIndex + ch] += extra;
                }

                if (!_amp.IsActive) { IsActive = false; return; }
            }
        }
    }
}
