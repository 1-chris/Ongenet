using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Kicka — a kick-drum synthesizer covering drumkit, trance, EDM, UK/happy-hardcore and hardstyle kicks
/// (plus the modern Zaag and Piep variations). Each hit is a one-shot, sample-accurate timeline split
/// into a <b>Tok</b> (transient: punch osc + click + tick, high-passed and saturated) and a <b>Tail</b>
/// (pitch-swept tone fed through a <see cref="DistortionStack"/> — the iterated EQ-boost → distort →
/// clean-up cycle that gives hardstyle its dense "scream"). A clean parallel sub keeps the low end solid,
/// the distorted tail can be stereo-widened and comb-filtered (Zaag), and a complementary crossover keeps
/// everything below ~120 Hz mono for club power. Stages are shaped by the time-pure
/// <see cref="CurveEnvelope"/>, so the inspector preview (<see cref="IPreviewRenderer"/>) matches playback.
/// </summary>
public sealed class KickaInstrument : PolyphonicInstrument, IPresetProvider, IPreviewRenderer
{
    public const string TypeId = "kicka";

    protected override string GetTypeId() => TypeId;

    // The note that plays the patch at its configured Base Pitch (the inspector keyboard starts here).
    private const int ReferenceNote = 60;

    private Parameter[]? _parameters;

    public KickaInstrument() : base(polyphony: 6) => Reset();

    public override string Name => "Kicka";

    // --- Pitch (tail sweep) ---
    public double StartPitch { get; set; }
    public double PitchDecayMs { get; set; }
    public double PitchCurve { get; set; }
    public double BasePitch { get; set; }

    // --- Tok (transient) ---
    public double PunchLevel { get; set; }
    public double PunchDecayMs { get; set; }
    public double PunchPitch { get; set; }
    public double PunchPitchDecayMs { get; set; }
    public double TokHp { get; set; }
    public double TokSat { get; set; }
    public double ClickLevel { get; set; }
    public double ClickDecayMs { get; set; }
    public double ClickTone { get; set; }
    public double TickLevel { get; set; }

    // --- Body (main tail tone → distortion stack) ---
    public int BodyWave { get; set; }
    public double BodyDecayMs { get; set; }
    public double BodyCurve { get; set; }
    public double BodyLevel { get; set; }

    // --- Sub (clean, parallel) ---
    public double SubLevel { get; set; }
    public double SubDecayMs { get; set; }

    // --- Distortion stack ---
    public int Stages { get; set; }
    public double StackDriveDb { get; set; }
    public double Scream { get; set; }
    public double Spread { get; set; }
    public double Boost { get; set; }
    public double StackQ { get; set; }
    public double Asymmetry { get; set; }
    public int Character { get; set; } // ShaperType
    public double StackTone { get; set; }
    public double StackMix { get; set; }

    // --- Tail tone layer (forward ring / reverse swell) ---
    public int TailMode { get; set; } // 0 Off, 1 Forward, 2 Reverse
    public double TailLengthMs { get; set; }
    public double TailPitch { get; set; }
    public double TailDecayMs { get; set; }
    public double TailCurve { get; set; }
    public double TailDrive { get; set; }
    public double TailLevel { get; set; }

    // --- Stereo / Zaag ---
    public double Width { get; set; }
    public double Comb { get; set; }
    public double CombFeedback { get; set; }
    public double MonoFreq { get; set; }

    // --- Output ---
    public double Gain { get; set; }
    public double Punch { get; set; }

    private static readonly string[] BodyWaveNames = { "Sine", "Triangle", "Saw" };
    private static readonly string[] CharacterNames = { "Tanh", "Hard Clip", "Foldback", "Sine Fold" };
    private static readonly string[] TailModeNames = { "Off", "Forward", "Reverse" };

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Start Pitch", 0, 48, () => StartPitch, v => StartPitch = v, "0", "st") { Group = "Pitch" },
        new FloatParameter("Pitch Decay", 1, 500, () => PitchDecayMs, v => PitchDecayMs = v, "0", "ms", skew: 2.0) { Group = "Pitch" },
        new FloatParameter("Pitch Curve", 0, 1, () => PitchCurve, v => PitchCurve = v, "0.00") { Group = "Pitch" },
        new FloatParameter("Base Pitch", 24, 72, () => BasePitch, v => BasePitch = Math.Round(v), "0", "st") { Group = "Pitch" },

        new FloatParameter("Punch", 0, 1, () => PunchLevel, v => PunchLevel = v, "0.00") { Group = "Tok" },
        new FloatParameter("Punch Decay", 5, 120, () => PunchDecayMs, v => PunchDecayMs = v, "0", "ms", skew: 2.0) { Group = "Tok" },
        new FloatParameter("Punch Pitch", 0, 48, () => PunchPitch, v => PunchPitch = v, "0", "st") { Group = "Tok" },
        new FloatParameter("Punch P.Dec", 2, 60, () => PunchPitchDecayMs, v => PunchPitchDecayMs = v, "0", "ms", skew: 2.0) { Group = "Tok" },
        new FloatParameter("HP", 50, 8000, () => TokHp, v => TokHp = v, "0", "Hz", skew: 3.0) { Group = "Tok" },
        new FloatParameter("Saturate", 0, 1, () => TokSat, v => TokSat = v, "0.00") { Group = "Tok" },
        new FloatParameter("Click", 0, 1, () => ClickLevel, v => ClickLevel = v, "0.00") { Group = "Tok" },
        new FloatParameter("Click Decay", 0.5, 50, () => ClickDecayMs, v => ClickDecayMs = v, "0.0", "ms", skew: 2.0) { Group = "Tok" },
        new FloatParameter("Click Tone", 200, 12000, () => ClickTone, v => ClickTone = v, "0", "Hz", skew: 3.0) { Group = "Tok" },
        new FloatParameter("Tick", 0, 1, () => TickLevel, v => TickLevel = v, "0.00") { Group = "Tok" },

        new ChoiceParameter("Wave", BodyWaveNames, () => BodyWave, i => BodyWave = i) { Group = "Body" },
        new FloatParameter("Decay", 10, 2000, () => BodyDecayMs, v => BodyDecayMs = v, "0", "ms", skew: 2.0) { Group = "Body" },
        new FloatParameter("Curve", 0, 1, () => BodyCurve, v => BodyCurve = v, "0.00") { Group = "Body" },
        new FloatParameter("Level", 0, 1, () => BodyLevel, v => BodyLevel = v, "0.00") { Group = "Body" },

        new FloatParameter("Level", 0, 1, () => SubLevel, v => SubLevel = v, "0.00") { Group = "Sub" },
        new FloatParameter("Decay", 10, 3000, () => SubDecayMs, v => SubDecayMs = v, "0", "ms", skew: 2.0) { Group = "Sub" },

        new FloatParameter("Stages", 0, DistortionStack.MaxStages, () => Stages, v => Stages = (int)Math.Round(v), "0") { Group = "Distortion" },
        new FloatParameter("Drive", 0, 24, () => StackDriveDb, v => StackDriveDb = v, "0.0", "dB") { Group = "Distortion" },
        new FloatParameter("Scream", 250, 4000, () => Scream, v => Scream = v, "0", "Hz", skew: 3.0) { Group = "Distortion" },
        new FloatParameter("Spread", 0, 2, () => Spread, v => Spread = v, "0.00") { Group = "Distortion" },
        new FloatParameter("Boost", 0, 18, () => Boost, v => Boost = v, "0.0", "dB") { Group = "Distortion" },
        new FloatParameter("Q", 0.4, 2.0, () => StackQ, v => StackQ = v, "0.00", "", skew: 2.0) { Group = "Distortion" },
        new FloatParameter("Asym", 0, 1, () => Asymmetry, v => Asymmetry = v, "0.00") { Group = "Distortion" },
        new ChoiceParameter("Character", CharacterNames, () => Character, i => Character = i) { Group = "Distortion" },
        new FloatParameter("Tone", 1500, 16000, () => StackTone, v => StackTone = v, "0", "Hz", skew: 3.0) { Group = "Distortion" },
        new FloatParameter("Mix", 0, 1, () => StackMix, v => StackMix = v, "0.00") { Group = "Distortion" },

        new ChoiceParameter("Mode", TailModeNames, () => TailMode, i => TailMode = i) { Group = "Tail" },
        new FloatParameter("Length", 10, 2000, () => TailLengthMs, v => TailLengthMs = v, "0", "ms", skew: 2.0) { Group = "Tail" },
        new FloatParameter("Pitch", -24, 12, () => TailPitch, v => TailPitch = v, "0", "st") { Group = "Tail" },
        new FloatParameter("Decay", 10, 2000, () => TailDecayMs, v => TailDecayMs = v, "0", "ms", skew: 2.0) { Group = "Tail" },
        new FloatParameter("Curve", 0, 1, () => TailCurve, v => TailCurve = v, "0.00") { Group = "Tail" },
        new FloatParameter("Drive", 0, 1, () => TailDrive, v => TailDrive = v, "0.00") { Group = "Tail" },
        new FloatParameter("Level", 0, 1, () => TailLevel, v => TailLevel = v, "0.00") { Group = "Tail" },

        new FloatParameter("Width", 0, 1, () => Width, v => Width = v, "0.00") { Group = "Stereo" },
        new FloatParameter("Comb", 0, 1, () => Comb, v => Comb = v, "0.00") { Group = "Stereo" },
        new FloatParameter("Comb FB", 0, 0.9, () => CombFeedback, v => CombFeedback = v, "0.00") { Group = "Stereo" },
        new FloatParameter("Mono Below", 60, 300, () => MonoFreq, v => MonoFreq = v, "0", "Hz", skew: 2.0) { Group = "Stereo" },

        new FloatParameter("Gain", 0, 1, () => Gain, v => Gain = v, "0.00") { Group = "Output" },
        new FloatParameter("Punch", 0, 1, () => Punch, v => Punch = v, "0.00") { Group = "Output" }
    };

    protected override Voice CreateVoice() => new KickVoice(this);

    // ===== Preview (IPreviewRenderer) =====

    public double PreviewSeconds => 1.5;

    public void RenderPreview(Span<float> mono, int sampleRate)
    {
        mono.Clear();
        if (mono.Length == 0) return;
        var format = new AudioFormat(sampleRate <= 0 ? 44100 : sampleRate, 1);
        var voice = new KickVoice(this);
        voice.Start(ReferenceNote, 1.0f, format);

        const int block = 512;
        var pos = 0;
        while (pos < mono.Length && voice.IsActive)
        {
            var n = Math.Min(block, mono.Length - pos);
            voice.Render(mono.Slice(pos, n));
            pos += n;
        }
    }

    public override IInstrument Clone()
    {
        var c = new KickaInstrument();
        CopyStateTo(c);
        return c;
    }

    private void CopyStateTo(KickaInstrument c)
    {
        c.StartPitch = StartPitch; c.PitchDecayMs = PitchDecayMs; c.PitchCurve = PitchCurve; c.BasePitch = BasePitch;
        c.PunchLevel = PunchLevel; c.PunchDecayMs = PunchDecayMs; c.PunchPitch = PunchPitch; c.PunchPitchDecayMs = PunchPitchDecayMs;
        c.TokHp = TokHp; c.TokSat = TokSat;
        c.ClickLevel = ClickLevel; c.ClickDecayMs = ClickDecayMs; c.ClickTone = ClickTone; c.TickLevel = TickLevel;
        c.BodyWave = BodyWave; c.BodyDecayMs = BodyDecayMs; c.BodyCurve = BodyCurve; c.BodyLevel = BodyLevel;
        c.SubLevel = SubLevel; c.SubDecayMs = SubDecayMs;
        c.Stages = Stages; c.StackDriveDb = StackDriveDb; c.Scream = Scream; c.Spread = Spread; c.Boost = Boost;
        c.StackQ = StackQ; c.Asymmetry = Asymmetry; c.Character = Character; c.StackTone = StackTone; c.StackMix = StackMix;
        c.TailMode = TailMode; c.TailLengthMs = TailLengthMs; c.TailPitch = TailPitch; c.TailDecayMs = TailDecayMs;
        c.TailCurve = TailCurve; c.TailDrive = TailDrive; c.TailLevel = TailLevel;
        c.Width = Width; c.Comb = Comb; c.CombFeedback = CombFeedback; c.MonoFreq = MonoFreq;
        c.Gain = Gain; c.Punch = Punch;
    }

    // ===== Presets =====

    private static readonly string[] PresetNamesList =
    {
        "Drumkit", "Trance Kick", "EDM Kick", "Hardstyle Kick", "UKHC Kick",
        "Oldschool Happy Hardcore Kick", "Hardstyle Zaag", "Hardstyle Piep"
    };

    public IReadOnlyList<string> PresetNames => PresetNamesList;

    public void LoadPreset(int index)
    {
        switch (index)
        {
            case 1: Trance(); break;
            case 2: Edm(); break;
            case 3: Hardstyle(); break;
            case 4: Ukhc(); break;
            case 5: HappyHardcore(); break;
            case 6: Zaag(); break;
            case 7: Piep(); break;
            default: Reset(); break; // 0 = Drumkit / init
        }
    }

    /// <summary>Init = a clean, fat, punchy drumkit kick (no distortion stack). All presets start here.</summary>
    private void Reset()
    {
        StartPitch = 22; PitchDecayMs = 32; PitchCurve = 0.6; BasePitch = 33; // ~A1, 55 Hz
        PunchLevel = 0.5; PunchDecayMs = 14; PunchPitch = 22; PunchPitchDecayMs = 14; TokHp = 150; TokSat = 0.2;
        ClickLevel = 0.5; ClickDecayMs = 6; ClickTone = 3500; TickLevel = 0.4;
        BodyWave = 0; BodyDecayMs = 240; BodyCurve = 0.7; BodyLevel = 0.9;
        SubLevel = 0.5; SubDecayMs = 260;
        Stages = 0; StackDriveDb = 6; Scream = 700; Spread = 1.0; Boost = 8; StackQ = 0.9;
        Asymmetry = 0.2; Character = (int)ShaperType.Tanh; StackTone = 6000; StackMix = 0.0;
        TailMode = 0; TailLengthMs = 400; TailPitch = -5; TailDecayMs = 350; TailCurve = 0.5; TailDrive = 0.4; TailLevel = 0.0;
        Width = 0.0; Comb = 0.0; CombFeedback = 0.7; MonoFreq = 120;
        Gain = 0.85; Punch = 0.35;
    }

    private void Trance()
    {
        Reset();
        StartPitch = 28; PitchDecayMs = 60; PitchCurve = 0.5;
        PunchLevel = 0.4; PunchPitch = 28; PunchDecayMs = 18;
        ClickLevel = 0.25; ClickDecayMs = 4; ClickTone = 2800; TickLevel = 0.2;
        BodyDecayMs = 340; BodyLevel = 0.95;
        SubLevel = 0.65; SubDecayMs = 400;
        Stages = 0; StackMix = 0.1;
        Gain = 0.85; Punch = 0.4;
    }

    private void Edm()
    {
        Reset();
        StartPitch = 24; PitchDecayMs = 35;
        PunchLevel = 0.6; PunchPitch = 24; PunchPitchDecayMs = 12;
        ClickLevel = 0.6; ClickDecayMs = 8; ClickTone = 5000; TickLevel = 0.5;
        BodyDecayMs = 280;
        SubLevel = 0.55; SubDecayMs = 260;
        Stages = 4; StackDriveDb = 5; Scream = 600; Spread = 0.8; Boost = 6; StackQ = 0.8;
        Asymmetry = 0.15; Character = (int)ShaperType.HardClip; StackTone = 7000; StackMix = 0.5;
        TailMode = 1; TailLengthMs = 140; TailPitch = 0; TailDecayMs = 140; TailCurve = 0.6; TailDrive = 0.4; TailLevel = 0.3;
        Width = 0.1;
        Gain = 0.85; Punch = 0.5;
    }

    private void Hardstyle()
    {
        Reset();
        StartPitch = 40; PitchDecayMs = 16; PitchCurve = 0.85;
        PunchLevel = 0.6; PunchPitch = 40; PunchPitchDecayMs = 8; TokHp = 200;
        ClickLevel = 0.5; ClickDecayMs = 5; ClickTone = 5200; TickLevel = 0.4;
        BodyWave = 1; BodyDecayMs = 200; BodyLevel = 0.9;
        SubLevel = 0.55; SubDecayMs = 240;
        Stages = 11; StackDriveDb = 8; Scream = 750; Spread = 1.0; Boost = 9; StackQ = 0.95;
        Asymmetry = 0.3; Character = (int)ShaperType.Foldback; StackTone = 6000; StackMix = 0.9;
        TailMode = 1; TailLengthMs = 650; TailPitch = -7; TailDecayMs = 600; TailCurve = 0.55; TailDrive = 0.85; TailLevel = 0.85;
        Width = 0.25; Comb = 0.15; CombFeedback = 0.7;
        Gain = 0.82; Punch = 0.6;
    }

    private void Ukhc()
    {
        Reset();
        StartPitch = 32; PitchDecayMs = 24;
        PunchLevel = 0.6; PunchPitch = 32; PunchPitchDecayMs = 12; TokHp = 180;
        ClickLevel = 0.55; ClickDecayMs = 6; ClickTone = 5500; TickLevel = 0.45;
        BodyWave = 0; BodyDecayMs = 200;
        SubLevel = 0.7; SubDecayMs = 230;
        Stages = 8; StackDriveDb = 6; Scream = 650; Spread = 0.9; Boost = 7; StackQ = 0.9;
        Asymmetry = 0.25; Character = (int)ShaperType.HardClip; StackTone = 5500; StackMix = 0.7;
        TailMode = 1; TailLengthMs = 180; TailPitch = -2; TailDecayMs = 170; TailCurve = 0.6; TailDrive = 0.6; TailLevel = 0.5;
        Width = 0.15; Comb = 0.1;
        Gain = 0.85; Punch = 0.55;
    }

    private void HappyHardcore()
    {
        Reset();
        StartPitch = 34; PitchDecayMs = 26;
        PunchLevel = 0.6; PunchPitch = 34; PunchPitchDecayMs = 14; TokHp = 160;
        ClickLevel = 0.55; ClickDecayMs = 6; ClickTone = 4500; TickLevel = 0.5;
        BodyWave = 0; BodyDecayMs = 210;
        SubLevel = 0.6; SubDecayMs = 230;
        Stages = 7; StackDriveDb = 6; Scream = 600; Spread = 0.85; Boost = 7; StackQ = 0.85;
        Asymmetry = 0.2; Character = (int)ShaperType.Tanh; StackTone = 7000; StackMix = 0.6;
        TailMode = 1; TailLengthMs = 200; TailPitch = 0; TailDecayMs = 190; TailCurve = 0.6; TailDrive = 0.5; TailLevel = 0.45;
        Width = 0.15; Comb = 0.05;
        Gain = 0.85; Punch = 0.55;
    }

    private void Zaag()
    {
        Hardstyle();
        BodyWave = 2; BodyDecayMs = 220; BodyLevel = 0.85;
        Stages = 12; StackDriveDb = 9; Scream = 800; Spread = 1.3; Boost = 9; StackQ = 1.0;
        Asymmetry = 0.35; Character = (int)ShaperType.Foldback; StackTone = 6500; StackMix = 0.9;
        SubLevel = 0.5; SubDecayMs = 220;
        TailLengthMs = 600; TailDecayMs = 550;
        Width = 0.5; Comb = 0.45; CombFeedback = 0.75;
        Gain = 0.80; Punch = 0.6;
    }

    private void Piep()
    {
        Hardstyle();
        StartPitch = 48; PitchDecayMs = 18;
        PunchLevel = 0.7; PunchPitch = 48; PunchPitchDecayMs = 22; TokHp = 220;
        ClickTone = 6000; TickLevel = 0.5;
        BodyWave = 1; BodyDecayMs = 200;
        SubLevel = 0.5; SubDecayMs = 220;
        Stages = 13; StackDriveDb = 9; Scream = 1100; Spread = 1.1; Boost = 10; StackQ = 1.1;
        Asymmetry = 0.3; Character = (int)ShaperType.SineFold; StackTone = 9000; StackMix = 0.9;
        TailLengthMs = 550; TailPitch = -5; TailDecayMs = 500; TailLevel = 0.8;
        Width = 0.4; Comb = 0.2; CombFeedback = 0.7;
        Gain = 0.80; Punch = 0.6;
    }

    /// <summary>One kick hit: Tok (transient) + Tail (pitch-swept tone → distortion stack) + clean sub.</summary>
    private sealed class KickVoice : Voice
    {
        private const float VoiceGain = 0.9f;
        private const double CombDelayMs = 1.2;

        private readonly KickaInstrument _inst;
        private readonly WaveOscillator _body = new();
        private readonly WaveOscillator _sub = new();
        private readonly WaveOscillator _noise = new();
        private readonly WaveOscillator _tick = new();
        private readonly WaveOscillator _tail = new();
        private readonly WaveOscillator _punch = new();
        private readonly DistortionStack _stack = new();
        private readonly CombFilter _comb = new();
        private readonly OnePole _tokHp = new();
        private readonly OnePole _xL = new();
        private readonly OnePole _xR = new();
        private Biquad _clickHp;
        private static uint _seed = 1;

        private CurveEnvelope _pitchEnv, _bodyEnv, _subEnv, _clickEnv, _tickEnv, _tailEnv, _punchEnv, _punchPitchEnv;
        private double _baseHz, _startPitch, _punchPitch;
        private long _elapsed, _totalSamples;
        private float _velocity;

        public KickVoice(KickaInstrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            var sr = format.SampleRate;
            var seed = _seed++ * 2654435761u + (uint)midiNote;

            _baseHz = MusicalMath.NoteToFrequency(_inst.BasePitch + (midiNote - ReferenceNote));
            _startPitch = _inst.StartPitch;
            _punchPitch = _inst.PunchPitch;

            foreach (var o in new[] { _body, _sub, _noise, _tick, _tail, _punch }) o.SetSampleRate(sr);
            _body.Wave = (OscWave)_inst.BodyWave; _body.ResetPhase(0);
            _sub.Wave = OscWave.Sine; _sub.SetFrequency(_baseHz); _sub.ResetPhase(0);
            _noise.Wave = OscWave.Noise; _noise.SeedNoise(seed);
            _tick.Wave = OscWave.Sine; _tick.ResetPhase(0);
            _tail.Wave = OscWave.Saw; _tail.SetFrequency(_baseHz * MusicalMath.SemitonesToRatio(_inst.TailPitch)); _tail.ResetPhase(0);
            _punch.Wave = OscWave.Sine; _punch.ResetPhase(0);

            _clickHp.Reset();
            _tokHp.SetLowpass(AudioMath.Clamp(_inst.TokHp, 20, sr * 0.45), sr); _tokHp.Reset();
            _xL.SetLowpass(AudioMath.Clamp(_inst.MonoFreq, 30, sr * 0.45), sr); _xL.Reset();
            _xR.SetLowpass(AudioMath.Clamp(_inst.MonoFreq, 30, sr * 0.45), sr); _xR.Reset();

            _stack.Configure(_inst.Stages, _inst.Scream, _inst.Spread, _inst.Boost, _inst.StackQ,
                _inst.StackDriveDb, _inst.Asymmetry, _inst.StackTone, (ShaperType)_inst.Character, sr);
            _stack.Reset();
            _comb.Configure(CombDelayMs, 0.16, _inst.CombFeedback, _inst.Comb, sr);
            _comb.Reset();

            _pitchEnv = new CurveEnvelope(0, 0, 0, _inst.PitchDecayMs / 1000.0, _inst.PitchCurve);
            _bodyEnv = new CurveEnvelope(0, 0.0005, 0, _inst.BodyDecayMs / 1000.0, _inst.BodyCurve);
            _subEnv = new CurveEnvelope(0, 0.0005, 0, _inst.SubDecayMs / 1000.0, _inst.BodyCurve);
            _clickEnv = new CurveEnvelope(0, 0.0003, 0, _inst.ClickDecayMs / 1000.0, 0.8);
            _tickEnv = new CurveEnvelope(0, 0.0003, 0, _inst.ClickDecayMs / 1000.0 * 1.5, 0.7);
            _tailEnv = new CurveEnvelope(0, 0.003, 0, _inst.TailDecayMs / 1000.0, _inst.TailCurve);
            _punchEnv = new CurveEnvelope(0, 0.0004, 0, _inst.PunchDecayMs / 1000.0, 0.7);
            _punchPitchEnv = new CurveEnvelope(0, 0, 0, _inst.PunchPitchDecayMs / 1000.0, 0.6);

            var punchTail = Math.Max(
                Math.Max(Math.Max(_bodyEnv.TotalSeconds, _subEnv.TotalSeconds), _clickEnv.TotalSeconds),
                Math.Max(_tickEnv.TotalSeconds, _punchEnv.TotalSeconds));
            // The tail (forward decay or reverse swell) sits in place after the punch, so include it either way.
            var total = _inst.TailMode != 0 ? Math.Max(punchTail, _tailEnv.TotalSeconds) : punchTail;
            _totalSamples = (long)((total + 0.02) * sr) + 1;
            _elapsed = 0;
        }

        // Kicks are one-shots: NoteOff is ignored; the voice ends on its own timeline.
        public override void Release() { }

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var sr = Format.SampleRate;

            var bodyLvl = (float)_inst.BodyLevel;
            var subLvl = (float)_inst.SubLevel;
            var clickLvl = (float)_inst.ClickLevel;
            var tickLvl = (float)_inst.TickLevel;
            var punchLvl = (float)_inst.PunchLevel;
            var tailLvl = (float)_inst.TailLevel;
            var tailDrive = (float)_inst.TailDrive;
            var tokSat = (float)_inst.TokSat;
            var stackMix = (float)_inst.StackMix;
            var combMix = (float)_inst.Comb;
            var width = (float)_inst.Width;
            var mode = _inst.TailMode;
            var amp = _velocity * (float)_inst.Gain * VoiceGain;
            var outDrive = 1f + (float)_inst.Punch * 2.5f;
            var stereo = channels >= 2 && (combMix > 0.001f || width > 0.001f);

            _tick.SetFrequency(AudioMath.Clamp(_inst.ClickTone, 20, sr * 0.45));
            var clickCoeffs = BiquadCoefficients.Compute(FilterMode.HighPass,
                AudioMath.Clamp(_inst.ClickTone, 20, sr * 0.45), 0.707, sr);

            for (var frame = 0; frame < frames; frame++)
            {
                var tp = _elapsed / (double)sr; // the punch is always on the beat (t = 0)

                // --- Tail tonal bus (body + tonal tail layer) → distortion stack ---
                double tonalDry = 0;
                {
                    var pe = _pitchEnv.Evaluate(tp);
                    _body.SetFrequency(AudioMath.Clamp(_baseHz * MusicalMath.SemitonesToRatio(_startPitch * pe), 1.0, sr * 0.49));
                    tonalDry += _body.Next() * (float)_bodyEnv.Evaluate(tp) * bodyLvl;
                }

                if (tailLvl > 0 && mode != 0)
                {
                    double tailAmp;
                    if (mode == 2) // reverse: the tail envelope played backwards → swells up under the kick
                    {
                        var tt = _tailEnv.TotalSeconds;
                        tailAmp = tp <= tt ? _tailEnv.Evaluate(tt - tp) : 0.0;
                    }
                    else tailAmp = _tailEnv.Evaluate(tp); // forward decay

                    if (tailAmp > 0) tonalDry += _tail.Next() * (float)tailAmp * tailLvl * (1f + tailDrive * 3f);
                }

                var scream = _stack.Process((float)tonalDry);
                float tonal = (float)(tonalDry * (1f - stackMix) + scream * stackMix);

                // --- Tok (transient): punch osc + click + tick, high-passed and saturated ---
                float tok = 0;
                double subClean = 0;
                if (tp >= 0)
                {
                    var ppe = _punchPitchEnv.Evaluate(tp);
                    _punch.SetFrequency(AudioMath.Clamp(_baseHz * MusicalMath.SemitonesToRatio(_punchPitch * ppe), 1.0, sr * 0.49));
                    tok += _punch.Next() * (float)_punchEnv.Evaluate(tp) * punchLvl;

                    if (clickLvl > 0)
                        tok += (float)_clickHp.Process(clickCoeffs, _noise.Next()) * (float)_clickEnv.Evaluate(tp) * clickLvl;
                    if (tickLvl > 0)
                        tok += _tick.Next() * (float)_tickEnv.Evaluate(tp) * tickLvl;

                    if (subLvl > 0) subClean += _sub.Next() * (float)_subEnv.Evaluate(tp) * subLvl;
                }

                tok = (float)_tokHp.ProcessHP(tok);
                if (tokSat > 0) tok = (float)Math.Tanh(tok * (1f + tokSat * 3f));

                // --- Stereo (Zaag) on the distorted tail, then a complementary mono-below crossover ---
                if (stereo)
                {
                    float hiL = tonal, hiR = tonal;
                    if (combMix > 0.001f) _comb.Process(tonal, tonal, out hiL, out hiR);

                    // Mid/side widen the stereo tail content.
                    var mid = 0.5f * (hiL + hiR);
                    var side = 0.5f * (hiL - hiR) * (1f + width * 2f);
                    hiL = mid + side;
                    hiR = mid - side;

                    float l = hiL + tok + (float)subClean;
                    float r = hiR + tok + (float)subClean;

                    // Keep everything below MonoFreq mono (club sub power); reconstructs phase-flat.
                    var loL = (float)_xL.ProcessLP(l);
                    var loR = (float)_xR.ProcessLP(r);
                    var lowMono = 0.5f * (loL + loR);
                    l = lowMono + (l - loL);
                    r = lowMono + (r - loR);

                    var sL = (float)Math.Tanh(l * amp * outDrive);
                    var sR = (float)Math.Tanh(r * amp * outDrive);
                    var bi = frame * channels;
                    buffer[bi] += sL;
                    buffer[bi + 1] += sR;
                    for (var c = 2; c < channels; c++) buffer[bi + c] += 0.5f * (sL + sR);
                }
                else
                {
                    var mono = tonal + tok + (float)subClean;
                    var s = (float)Math.Tanh(mono * amp * outDrive);
                    var bi = frame * channels;
                    for (var c = 0; c < channels; c++) buffer[bi + c] += s;
                }

                if (++_elapsed >= _totalSamples) { IsActive = false; return; }
            }
        }
    }
}
