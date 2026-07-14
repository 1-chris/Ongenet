using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments.Drums;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A single preset-driven drum synthesizer covering classic drum-model variants. One engine
/// combines pitch-swept tones (<see cref="KickaInstrument"/>), filtered noise bursts
/// (<see cref="PercaInstrument"/>), <see cref="DistortionStack"/> drive and resonant
/// <see cref="Biquad"/> filtering. Pick a model to load its internal recipe; macro knobs
/// (Pitch, Decay, Tone, Noise, Drive, Gain) tweak the result on top.
/// </summary>
public sealed class DrumModelInstrument : PolyphonicInstrument
{
    public const string TypeId = "drum_model";

    protected override string GetTypeId() => TypeId;

    private const int ReferenceNote = 60;

    private static readonly string[] ModelNames =
    {
        "v0 Cymbal", "v0 Hat", "v0 Kick", "v0 Snare", "v0 Tom", "v0 Zap Kick",
        "v1 Clap", "v1 Cowbell", "v1 Hat", "v1 Kick", "v1 Snare", "v1 Tom",
        "v8 Clap", "v8 Claves", "v8 Cowbell", "v8 Cymbal", "v8 Hat", "v8 Kick",
        "v8 Maracas", "v8 Rimshot", "v8 Snare", "v8 Tom",
        "v9 Clap", "v9 Crash", "v9 Hat Closed", "v9 Hat Open", "v9 Kick", "v9 Ride",
        "v9 Rimshot", "v9 Snare", "v9 Tom",
        "Kick", "Perc"
    };

    private Parameter[]? _parameters;

    public DrumModelInstrument() : base(polyphony: 12)
    {
        Model = 0;
        ApplyModel(0);
    }

    public override string Name => "Drum Model";

    public int Model { get; private set; }

    // User macros
    public double Pitch { get; set; } = 0.5;
    public double Decay { get; set; } = 0.5;
    public double Tone { get; set; } = 0.5;
    public double Noise { get; set; } = 0.5;
    public double Drive { get; set; } = 0.5;
    public double Gain { get; set; } = 0.85;

    // Internal engine (set by ApplyModel)
    internal bool UseKickBody;
    internal bool UseNoise;
    internal int BodyWave;
    internal double StartPitch;
    internal double PitchDecayMs;
    internal double PitchCurve;
    internal double BodyDecayMs;
    internal double BodyLevel;
    internal int FilterType;
    internal double Cutoff;
    internal double Resonance;
    internal double Color;
    internal double AttackMs;
    internal double DecayMs;
    internal double Curve;
    internal int Taps;
    internal double SpreadMs;
    internal double TapDecayMs;
    internal double ToneLevel;
    internal int ToneType;
    internal double ToneFreq;
    internal double ToneDecayMs;
    internal int DistStages;
    internal double DistDriveDb;
    internal double DistScream;
    internal double DistMix;
    internal double Width;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Model", ModelNames, () => Model, i => ApplyModel(i)) { Group = "Model" },
        new FloatParameter("Pitch", 0, 1, () => Pitch, v => Pitch = v, "0.00") { Group = "Macros" },
        new FloatParameter("Decay", 0, 1, () => Decay, v => Decay = v, "0.00") { Group = "Macros" },
        new FloatParameter("Tone", 0, 1, () => Tone, v => Tone = v, "0.00") { Group = "Macros" },
        new FloatParameter("Noise", 0, 1, () => Noise, v => Noise = v, "0.00") { Group = "Macros" },
        new FloatParameter("Drive", 0, 1, () => Drive, v => Drive = v, "0.00") { Group = "Macros" },
        new FloatParameter("Gain", 0, 1, () => Gain, v => Gain = v, "0.00") { Group = "Macros" }
    };

    protected override Voice CreateVoice() => new DrumModelVoice(this);

    public override IInstrument Clone()
    {
        var c = new DrumModelInstrument();
        c.ApplyModel(Model);
        c.Pitch = Pitch;
        c.Decay = Decay;
        c.Tone = Tone;
        c.Noise = Noise;
        c.Drive = Drive;
        c.Gain = Gain;
        return c;
    }

    internal void ApplyModel(int index)
    {
        Model = Math.Clamp(index, 0, ModelNames.Length - 1);
        ResetEngine();
        switch (Model)
        {
            case 0: V0Cymbal(); break;
            case 1: V0Hat(); break;
            case 2: V0Kick(); break;
            case 3: V0Snare(); break;
            case 4: V0Tom(); break;
            case 5: V0ZapKick(); break;
            case 6: V1Clap(); break;
            case 7: V1Cowbell(); break;
            case 8: V1Hat(); break;
            case 9: V1Kick(); break;
            case 10: V1Snare(); break;
            case 11: V1Tom(); break;
            case 12: V8Clap(); break;
            case 13: V8Claves(); break;
            case 14: V8Cowbell(); break;
            case 15: V8Cymbal(); break;
            case 16: V8Hat(); break;
            case 17: V8Kick(); break;
            case 18: V8Maracas(); break;
            case 19: V8Rimshot(); break;
            case 20: V8Snare(); break;
            case 21: V8Tom(); break;
            case 22: V9Clap(); break;
            case 23: V9Crash(); break;
            case 24: V9HatClosed(); break;
            case 25: V9HatOpen(); break;
            case 26: V9Kick(); break;
            case 27: V9Ride(); break;
            case 28: V9Rimshot(); break;
            case 29: V9Snare(); break;
            case 30: V9Tom(); break;
            case 31: KickLike(); break;
            default: PercLike(); break;
        }
    }

    private void ResetEngine()
    {
        UseKickBody = false;
        UseNoise = true;
        BodyWave = 0;
        StartPitch = 0;
        PitchDecayMs = 40;
        PitchCurve = 0.65;
        BodyDecayMs = 200;
        BodyLevel = 0.8;
        FilterType = 1;
        Cutoff = 4000;
        Resonance = 1.0;
        Color = 0.1;
        AttackMs = 0.3;
        DecayMs = 200;
        Curve = 0.7;
        Taps = 1;
        SpreadMs = 10;
        TapDecayMs = 30;
        ToneLevel = 0;
        ToneType = 0;
        ToneFreq = 800;
        ToneDecayMs = 120;
        DistStages = 0;
        DistDriveDb = 4;
        DistScream = 900;
        DistMix = 0;
        Width = 0.25;
    }

    // ---- Model presets (v0/v1/v8/v9 families) ----

    private void V0Cymbal() { UseNoise = true; FilterType = 0; Cutoff = 6200; DecayMs = 1400; Curve = 0.3; ToneLevel = 0.3; ToneType = 1; ToneFreq = 7000; ToneDecayMs = 1200; Width = 0.65; }
    private void V0Hat() { UseNoise = true; FilterType = 0; Cutoff = 9000; DecayMs = 70; Curve = 0.85; ToneLevel = 0.35; ToneType = 1; ToneFreq = 7500; ToneDecayMs = 55; }
    private void V0Kick() { UseKickBody = true; UseNoise = false; StartPitch = 28; PitchDecayMs = 38; PitchCurve = 0.7; BodyDecayMs = 280; BodyLevel = 0.95; DistStages = 2; DistMix = 0.25; }
    private void V0Snare() { UseNoise = true; UseKickBody = false; FilterType = 1; Cutoff = 1800; DecayMs = 220; Taps = 2; SpreadMs = 7; ToneLevel = 0.75; ToneFreq = 190; ToneDecayMs = 140; DistStages = 2; DistMix = 0.25; }
    private void V0Tom() { UseKickBody = true; UseNoise = false; StartPitch = 18; PitchDecayMs = 55; BodyDecayMs = 320; BodyLevel = 0.95; }
    private void V0ZapKick() { UseKickBody = true; UseNoise = false; StartPitch = 42; PitchDecayMs = 22; PitchCurve = 0.85; BodyDecayMs = 260; DistStages = 3; DistDriveDb = 6; DistMix = 0.35; }

    private void V1Clap() { UseNoise = true; FilterType = 1; Cutoff = 1700; DecayMs = 360; Taps = 3; SpreadMs = 11; TapDecayMs = 28; DriveMacroDefault(); }
    private void V1Cowbell() { UseNoise = false; ToneLevel = 0.95; ToneType = 1; ToneFreq = 780; ToneDecayMs = 180; FilterType = 1; Cutoff = 2200; Resonance = 2.5; DecayMs = 200; }
    private void V1Hat() { UseNoise = true; FilterType = 0; Cutoff = 8200; DecayMs = 85; ToneLevel = 0.4; ToneType = 1; ToneFreq = 6800; ToneDecayMs = 65; }
    private void V1Kick() { UseKickBody = true; UseNoise = false; StartPitch = 26; PitchDecayMs = 42; PitchCurve = 0.68; BodyDecayMs = 300; BodyLevel = 0.93; DistStages = 2; DistDriveDb = 5; DistMix = 0.3; }
    private void V1Snare() { UseNoise = true; FilterType = 1; Cutoff = 2000; DecayMs = 240; Taps = 2; ToneLevel = 0.8; ToneFreq = 200; ToneDecayMs = 150; DistStages = 3; DistMix = 0.3; }
    private void V1Tom() { UseKickBody = true; UseNoise = false; StartPitch = 14; PitchDecayMs = 48; BodyDecayMs = 300; ToneFreq = 140; }

    private void V8Clap() { UseNoise = true; FilterType = 1; Cutoff = 1400; DecayMs = 400; Taps = 3; SpreadMs = 12; TapDecayMs = 30; Width = 0.55; }
    private void V8Claves() { UseNoise = false; ToneLevel = 0.9; ToneFreq = 1650; ToneDecayMs = 55; FilterType = 1; Cutoff = 1900; Resonance = 2.2; DecayMs = 65; AttackMs = 0.1; }
    private void V8Cowbell() { UseNoise = false; ToneLevel = 0.92; ToneType = 1; ToneFreq = 620; ToneDecayMs = 220; FilterType = 1; Cutoff = 1800; Resonance = 3.0; DecayMs = 240; }
    private void V8Cymbal() { UseNoise = true; FilterType = 0; Cutoff = 5800; DecayMs = 1600; Curve = 0.28; ToneLevel = 0.28; ToneType = 1; ToneFreq = 6500; Width = 0.7; }
    private void V8Hat() { UseNoise = true; FilterType = 0; Cutoff = 8600; DecayMs = 60; Curve = 0.88; ToneLevel = 0.38; ToneType = 1; ToneFreq = 7200; ToneDecayMs = 48; }
    private void V8Kick() { UseKickBody = true; UseNoise = false; StartPitch = 24; PitchDecayMs = 40; PitchCurve = 0.66; BodyDecayMs = 290; BodyLevel = 0.94; DistStages = 3; DistDriveDb = 6; DistMix = 0.32; }
    private void V8Maracas() { UseNoise = true; FilterType = 0; Cutoff = 7000; DecayMs = 130; Curve = 0.6; ToneLevel = 0.15; Width = 0.55; }
    private void V8Rimshot() { UseNoise = true; FilterType = 1; Cutoff = 3000; DecayMs = 95; Taps = 2; SpreadMs = 4; ToneLevel = 0.55; ToneType = 1; ToneFreq = 2100; ToneDecayMs = 45; DistStages = 2; DistMix = 0.2; }
    private void V8Snare() { UseNoise = true; FilterType = 1; Cutoff = 1750; DecayMs = 270; Taps = 2; ToneLevel = 0.85; ToneFreq = 180; ToneDecayMs = 160; DistStages = 2; DistMix = 0.28; }
    private void V8Tom() { UseKickBody = true; UseNoise = false; StartPitch = 16; PitchDecayMs = 50; BodyDecayMs = 340; BodyWave = 0; }

    private void V9Clap() { UseNoise = true; FilterType = 1; Cutoff = 1600; DecayMs = 420; Taps = 3; SpreadMs = 10; TapDecayMs = 26; Width = 0.6; DistStages = 1; DistMix = 0.15; }
    private void V9Crash() { UseNoise = true; FilterType = 0; Cutoff = 5400; DecayMs = 1900; Curve = 0.25; ToneLevel = 0.32; ToneType = 1; ToneFreq = 6800; Width = 0.75; }
    private void V9HatClosed() { UseNoise = true; FilterType = 0; Cutoff = 9200; DecayMs = 55; Curve = 0.9; ToneLevel = 0.42; ToneType = 1; ToneFreq = 7800; ToneDecayMs = 42; }
    private void V9HatOpen() { V9HatClosed(); DecayMs = 480; ToneDecayMs = 380; Curve = 0.45; Width = 0.55; }
    private void V9Kick() { UseKickBody = true; UseNoise = false; StartPitch = 22; PitchDecayMs = 36; PitchCurve = 0.72; BodyDecayMs = 270; BodyLevel = 0.96; DistStages = 3; DistDriveDb = 7; DistMix = 0.38; }
    private void V9Ride() { UseNoise = true; FilterType = 0; Cutoff = 8800; DecayMs = 220; ToneLevel = 0.5; ToneType = 1; ToneFreq = 7600; ToneDecayMs = 180; Width = 0.45; }
    private void V9Rimshot() { UseNoise = true; FilterType = 1; Cutoff = 3200; DecayMs = 88; Taps = 2; SpreadMs = 3; ToneLevel = 0.58; ToneType = 1; ToneFreq = 2400; DistStages = 2; DistMix = 0.22; }
    private void V9Snare() { UseNoise = true; FilterType = 1; Cutoff = 1900; DecayMs = 255; Taps = 2; SpreadMs = 8; ToneLevel = 0.82; ToneFreq = 195; ToneDecayMs = 145; DistStages = 3; DistMix = 0.32; }
    private void V9Tom() { UseKickBody = true; UseNoise = false; StartPitch = 12; PitchDecayMs = 44; BodyDecayMs = 310; BodyLevel = 0.92; }

    private void KickLike()
    {
        UseKickBody = true;
        UseNoise = false;
        StartPitch = 24;
        PitchDecayMs = 35;
        PitchCurve = 0.65;
        BodyDecayMs = 260;
        BodyLevel = 0.95;
        DistStages = 3;
        DistDriveDb = 7;
        DistScream = 750;
        DistMix = 0.4;
        Width = 0.1;
    }

    private void PercLike()
    {
        UseNoise = true;
        FilterType = 1;
        Cutoff = 1500;
        Resonance = 1.8;
        DecayMs = 380;
        Taps = 3;
        SpreadMs = 11;
        TapDecayMs = 28;
        Width = 0.6;
    }

    private void DriveMacroDefault() { DistStages = 1; DistDriveDb = 3; DistMix = 0.12; }

    /// <summary>One drum hit combining kick body, filtered noise and optional tone layer.</summary>
    private sealed class DrumModelVoice : DrumVoice
    {
        private readonly DrumModelInstrument _inst;
        private readonly WaveOscillator _body = new();
        private readonly WaveOscillator _noiseA = new();
        private readonly WaveOscillator _noiseB = new();
        private readonly WaveOscillator _toneSine = new();
        private readonly WaveOscillator[] _metal = new WaveOscillator[MetalRatios.Length];
        private readonly DistortionStack _stack = new();
        private readonly OnePole _colorA = new();
        private readonly OnePole _colorB = new();
        private readonly CurveEnvelope[] _tapEnvs = new CurveEnvelope[MaxTaps];
        private Biquad _filtA;
        private Biquad _filtB;

        private CurveEnvelope _pitchEnv;
        private CurveEnvelope _bodyEnv;
        private CurveEnvelope _toneEnv;
        private int _taps;
        private double _baseHz;
        private double _pitchRatio;
        private double _decayScale;
        private double _toneScale;
        private double _noiseScale;
        private double _driveScale;

        public DrumModelVoice(DrumModelInstrument inst)
        {
            _inst = inst;
            for (var i = 0; i < _metal.Length; i++) _metal[i] = new WaveOscillator { Wave = OscWave.Square };
        }

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            Velocity = velocity;
            var sr = format.SampleRate;
            var seed = NextSeed(midiNote);

            _pitchRatio = MusicalMath.SemitonesToRatio((midiNote - ReferenceNote) + (_inst.Pitch - 0.5) * 24.0);
            _baseHz = MusicalMath.NoteToFrequency(ReferenceNote) * _pitchRatio;
            _decayScale = 0.25 + _inst.Decay * 1.75;
            _toneScale = _inst.Tone * 2.0;
            _noiseScale = _inst.Noise * 2.0;
            _driveScale = _inst.Drive * 2.0;

            _body.SetSampleRate(sr);
            _body.Wave = (OscWave)_inst.BodyWave;
            _body.ResetPhase(0);

            _noiseA.Wave = OscWave.Noise;
            _noiseA.SetSampleRate(sr);
            _noiseA.SeedNoise(seed);
            _noiseB.Wave = OscWave.Noise;
            _noiseB.SetSampleRate(sr);
            _noiseB.SeedNoise(seed * 747796405u + 1u);

            _toneSine.Wave = OscWave.Sine;
            _toneSine.SetSampleRate(sr);
            var toneHz = _inst.ToneFreq * _pitchRatio;
            _toneSine.SetFrequency(AudioMath.Clamp(toneHz, 10, sr * 0.45));
            _toneSine.ResetPhase(0);
            for (var i = 0; i < _metal.Length; i++)
            {
                _metal[i].SetSampleRate(sr);
                _metal[i].SetFrequency(AudioMath.Clamp(toneHz * MetalRatios[i], 10, sr * 0.45));
                _metal[i].ResetPhase(i / (double)_metal.Length);
            }

            var colorHz = 18000.0 * Math.Pow(1500.0 / 18000.0, AudioMath.Clamp(_inst.Color, 0, 1));
            _colorA.SetLowpass(AudioMath.Clamp(colorHz, 100, sr * 0.45), sr);
            _colorA.Reset();
            _colorB.SetLowpass(AudioMath.Clamp(colorHz, 100, sr * 0.45), sr);
            _colorB.Reset();
            _filtA.Reset();
            _filtB.Reset();

            _stack.Configure(_inst.DistStages, _inst.DistScream, 1.0, 6, 0.9,
                _inst.DistDriveDb * _driveScale, 0.2, 6000, ShaperType.Tanh, sr);
            _stack.Reset();

            _pitchEnv = new CurveEnvelope(0, 0.001, 0, _inst.PitchDecayMs * _decayScale / 1000.0, _inst.PitchCurve);
            _bodyEnv = new CurveEnvelope(0, 0.0005, 0, _inst.BodyDecayMs * _decayScale / 1000.0, 0.7);

            var attack = _inst.AttackMs / 1000.0;
            var spread = _inst.SpreadMs / 1000.0;
            _taps = Math.Clamp(_inst.Taps, 1, MaxTaps);
            for (var i = 0; i < _taps; i++)
            {
                var isLast = i == _taps - 1;
                var decay = (isLast ? _inst.DecayMs : _inst.TapDecayMs) * _decayScale / 1000.0;
                _tapEnvs[i] = new CurveEnvelope(i * spread, attack, 0, decay, _inst.Curve);
            }

            _toneEnv = new CurveEnvelope((_taps - 1) * spread, attack, 0, _inst.ToneDecayMs * _decayScale / 1000.0, _inst.Curve);

            var total = _tapEnvs[_taps - 1].TotalSeconds;
            if (_inst.UseKickBody) total = Math.Max(total, _bodyEnv.TotalSeconds);
            if (_inst.ToneLevel > 0) total = Math.Max(total, _toneEnv.TotalSeconds);
            BeginTimeline(total, sr);
        }

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var sr = Format.SampleRate;

            var bodyLvl = (float)(_inst.BodyLevel * _toneScale);
            var toneLvl = (float)(_inst.ToneLevel * _toneScale);
            var metal = _inst.ToneType == 1;
            var stackMix = (float)Math.Clamp(_inst.DistMix * _driveScale, 0, 1);
            var width = (float)_inst.Width;
            var amp = Velocity * (float)_inst.Gain * VoiceGain;
            var stereo = channels >= 2 && width > 0.001f;

            var mode = _inst.FilterType switch
            {
                0 => FilterMode.HighPass,
                2 => FilterMode.LowPass,
                _ => FilterMode.BandPass
            };
            var coeffs = BiquadCoefficients.Compute(mode,
                AudioMath.Clamp(_inst.Cutoff * _pitchRatio, 20, sr * 0.45),
                AudioMath.Clamp(_inst.Resonance, 0.05, 12), sr);

            for (var frame = 0; frame < frames; frame++)
            {
                var t = Elapsed / (double)sr;
                double sample = 0;

                if (_inst.UseKickBody)
                {
                    var pe = _pitchEnv.Evaluate(t);
                    _body.SetFrequency(AudioMath.Clamp(_baseHz * MusicalMath.SemitonesToRatio(_inst.StartPitch * pe), 1.0, sr * 0.49));
                    var body = _body.Next() * (float)_bodyEnv.Evaluate(t) * bodyLvl;
                    var scream = _stack.Process(body);
                    sample += body * (1.0 - stackMix) + scream * stackMix;
                }

                if (_inst.UseNoise)
                {
                    var env = 0.0;
                    for (var i = 0; i < _taps; i++)
                    {
                        var e = _tapEnvs[i].Evaluate(t);
                        if (e > env) env = e;
                    }

                    env *= _noiseScale;

                    var tone = 0.0;
                    if (toneLvl > 0)
                    {
                        if (metal)
                        {
                            double m = 0;
                            for (var i = 0; i < _metal.Length; i++) m += _metal[i].Next();
                            tone = m * (1.0 / _metal.Length);
                        }
                        else tone = _toneSine.Next();

                        tone *= _toneEnv.Evaluate(t) * toneLvl;
                    }

                    var a = (float)(_filtA.Process(coeffs, _colorA.ProcessLP(_noiseA.Next()) * env) + tone);
                    var b = stereo
                        ? (float)(_filtB.Process(coeffs, _colorB.ProcessLP(_noiseB.Next()) * env) + tone)
                        : a;

                    if (stereo)
                    {
                        var mid = 0.5f * (a + b);
                        var l = (mid + width * (a - mid)) * amp;
                        var r = (mid + width * (b - mid)) * amp;
                        WriteStereo(buffer, frame, channels, l + (float)sample, r + (float)sample);
                    }
                    else
                    {
                        WriteMono(buffer, frame, channels, (a + (float)sample) * amp);
                    }
                }
                else
                {
                    if (toneLvl > 0)
                    {
                        double tone;
                        if (metal)
                        {
                            double m = 0;
                            for (var i = 0; i < _metal.Length; i++) m += _metal[i].Next();
                            tone = m * (1.0 / _metal.Length);
                        }
                        else tone = _toneSine.Next();

                        tone *= _toneEnv.Evaluate(t) * toneLvl;
                        sample += (float)_filtA.Process(coeffs, tone);
                    }

                    WriteMono(buffer, frame, channels, (float)sample * amp);
                }

                if (AdvanceTimeline()) return;
            }
        }
    }
}
