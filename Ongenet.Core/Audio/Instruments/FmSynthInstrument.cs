using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Four-operator FM synthesizer with a full modulation matrix, filtered noise source, per-operator
/// waveforms, mod envelope, multimode output filter, and global performance controls.
/// </summary>
public sealed class FmSynthInstrument : PolyphonicInstrument, IPresetProvider
{
    public const string TypeId = "fmsynth";
    private const int OpCount = 4;
    private const double TwoPi = 2.0 * Math.PI;

    private static readonly string[] WaveNames = { "Sine", "Triangle", "Saw", "Square" };
    private static readonly string[] FilterModeNames = { "Low pass", "Band pass", "High pass" };

    protected override string GetTypeId() => TypeId;

    public FmSynthInstrument() : base(polyphony: 16) => Reset();

    public override string Name => "FM Synth";

    /// <summary>Per-operator settings (index 0 = Op 1 … 3 = Op 4).</summary>
    public FmOperatorSettings[] Operators { get; } =
    {
        new(), new(), new(), new()
    };

    /// <summary>FM matrix [source op 0–3][destination op 0–3], 0..1 (UI shows 0–999).</summary>
    public double[,] Matrix { get; } = new double[OpCount, OpCount];

    /// <summary>Noise modulation amount into each operator (0..1).</summary>
    public double[] NoiseToOp { get; } = new double[OpCount];

    public double NoiseLevel { get; set; }
    public double NoiseModLevel { get; set; } = 1.0;
    public bool NoiseModEnabled { get; set; } = true;
    public double NoiseCutoff { get; set; } = 8000;
    public double NoiseResonance { get; set; } = 0.7;
    public double NoiseDrive { get; set; }

    /// <summary>Scales the entire FM matrix depth (built-in mod envelope — no external modulator required).</summary>
    public double ModEnvAmount { get; set; } = 1.0;
    public double ModAttackSeconds { get; set; } = 0.005;
    public double ModDecaySeconds { get; set; } = 0.3;
    public double ModSustainLevel { get; set; } = 0.0;
    public double ModReleaseSeconds { get; set; } = 0.4;

    public bool FilterOn { get; set; } = true;
    public int FilterModeIndex { get; set; }
    public double Cutoff { get; set; } = 12000;
    public double Resonance { get; set; } = 0.8;
    public double FilterEnvAmount { get; set; } = 0.35;
    public double FilterAttackSeconds { get; set; } = 0.005;
    public double FilterDecaySeconds { get; set; } = 0.25;
    public double FilterSustainLevel { get; set; } = 0.2;
    public double FilterReleaseSeconds { get; set; } = 0.3;

    public double AttackSeconds { get; set; } = 0.005;
    public double DecaySeconds { get; set; } = 0.12;
    public double SustainLevel { get; set; } = 0.7;
    public double ReleaseSeconds { get; set; } = 0.25;
    public double AmpVelocitySens { get; set; } = 1.0;

    public double PitchSemitones { get; set; }
    public double GlideMs { get; set; }
    public double Gain { get; set; } = 0.75;
    public double Pan { get; set; }
    /// <summary>How much MIDI velocity scales FM matrix depth (0 = none, 1 = full).</summary>
    public double VelocityToIndex { get; set; } = 0.65;

    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= BuildParameters();

    private IReadOnlyList<Parameter> BuildParameters()
    {
        var list = new List<Parameter>(80);
        for (var i = 0; i < OpCount; i++)
        {
            var op = i;
            var g = $"Op {op + 1}";
            list.Add(new FloatParameter("Ratio", 0.25, 16, () => Operators[op].Ratio, v => Operators[op].Ratio = v, "0.00") { Group = g });
            list.Add(new FloatParameter("Offset", -999, 999, () => Operators[op].OffsetHz, v => Operators[op].OffsetHz = v, "0", "Hz") { Group = g });
            list.Add(new ChoiceParameter("Wave", WaveNames, () => Operators[op].WaveIndex, v => Operators[op].WaveIndex = v) { Group = g });
            list.Add(new FloatParameter("Mod", 0, 1, () => Operators[op].ModLevel, v => Operators[op].ModLevel = v, "0.00") { Group = g });
            list.Add(new BoolParameter("Mod On", () => Operators[op].ModEnabled, v => Operators[op].ModEnabled = v) { Group = g });
            list.Add(new FloatParameter("Level", 0, 1, () => Operators[op].Level, v => Operators[op].Level = v, "0.00") { Group = g });
            list.Add(new FloatParameter("Pan", -1, 1, () => Operators[op].Pan, v => Operators[op].Pan = v, "0.00") { Group = g });
        }

        for (var src = 0; src < OpCount; src++)
        {
            for (var dst = 0; dst < OpCount; dst++)
            {
                var s = src;
                var d = dst;
                list.Add(new FloatParameter($"{src + 1}→{dst + 1}", 0, 999,
                    () => Matrix[s, d] * 999.0, v => Matrix[s, d] = v / 999.0, "0") { Group = "Matrix" });
            }
        }

        for (var dst = 0; dst < OpCount; dst++)
        {
            var d = dst;
            list.Add(new FloatParameter($"N→{dst + 1}", 0, 999,
                () => NoiseToOp[d] * 999.0, v => NoiseToOp[d] = v / 999.0, "0") { Group = "Matrix" });
        }

        list.Add(new FloatParameter("Level", 0, 1, () => NoiseLevel, v => NoiseLevel = v, "0.00") { Group = "Noise" });
        list.Add(new FloatParameter("Mod", 0, 1, () => NoiseModLevel, v => NoiseModLevel = v, "0.00") { Group = "Noise" });
        list.Add(new BoolParameter("Mod On", () => NoiseModEnabled, v => NoiseModEnabled = v) { Group = "Noise" });
        list.Add(new FloatParameter("Cutoff", 80, 18000, () => NoiseCutoff, v => NoiseCutoff = v, "0", "Hz", skew: 3.0) { Group = "Noise" });
        list.Add(new FloatParameter("Reso", 0.5, 12, () => NoiseResonance, v => NoiseResonance = v, "0.0") { Group = "Noise" });
        list.Add(new FloatParameter("Drive", 0, 1, () => NoiseDrive, v => NoiseDrive = v, "0.00") { Group = "Noise" });

        list.Add(new FloatParameter("Amount", 0, 1, () => ModEnvAmount, v => ModEnvAmount = v, "0.00") { Group = "Mod Envelope" });
        list.Add(new FloatParameter("Attack", 0.001, 4, () => ModAttackSeconds, v => ModAttackSeconds = v, "0.000", "s") { Group = "Mod Envelope" });
        list.Add(new FloatParameter("Decay", 0.001, 4, () => ModDecaySeconds, v => ModDecaySeconds = v, "0.000", "s") { Group = "Mod Envelope" });
        list.Add(new FloatParameter("Sustain", 0, 1, () => ModSustainLevel, v => ModSustainLevel = v, "0.00") { Group = "Mod Envelope" });
        list.Add(new FloatParameter("Release", 0.001, 6, () => ModReleaseSeconds, v => ModReleaseSeconds = v, "0.000", "s") { Group = "Mod Envelope" });

        list.Add(new BoolParameter("On", () => FilterOn, v => FilterOn = v) { Group = "Filter" });
        list.Add(new ChoiceParameter("Mode", FilterModeNames, () => FilterModeIndex, v => FilterModeIndex = v) { Group = "Filter" });
        list.Add(new FloatParameter("Cutoff", 80, 18000, () => Cutoff, v => Cutoff = v, "0", "Hz", skew: 3.0) { Group = "Filter" });
        list.Add(new FloatParameter("Reso", 0.5, 16, () => Resonance, v => Resonance = v, "0.0") { Group = "Filter" });
        list.Add(new FloatParameter("Env Amt", -1, 1, () => FilterEnvAmount, v => FilterEnvAmount = v, "0.00") { Group = "Filter" });

        list.Add(new FloatParameter("Attack", 0.001, 2, () => FilterAttackSeconds, v => FilterAttackSeconds = v, "0.000", "s") { Group = "Filter Envelope" });
        list.Add(new FloatParameter("Decay", 0.001, 2, () => FilterDecaySeconds, v => FilterDecaySeconds = v, "0.000", "s") { Group = "Filter Envelope" });
        list.Add(new FloatParameter("Sustain", 0, 1, () => FilterSustainLevel, v => FilterSustainLevel = v, "0.00") { Group = "Filter Envelope" });
        list.Add(new FloatParameter("Release", 0.001, 3, () => FilterReleaseSeconds, v => FilterReleaseSeconds = v, "0.000", "s") { Group = "Filter Envelope" });

        list.Add(new FloatParameter("Attack", 0.001, 2, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s") { Group = "Amp Envelope" });
        list.Add(new FloatParameter("Decay", 0.001, 2, () => DecaySeconds, v => DecaySeconds = v, "0.000", "s") { Group = "Amp Envelope" });
        list.Add(new FloatParameter("Sustain", 0, 1, () => SustainLevel, v => SustainLevel = v, "0.00") { Group = "Amp Envelope" });
        list.Add(new FloatParameter("Release", 0.001, 3, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s") { Group = "Amp Envelope" });
        list.Add(new FloatParameter("Vel Sens", 0, 1, () => AmpVelocitySens, v => AmpVelocitySens = v, "0.00") { Group = "Amp Envelope" });

        list.Add(new FloatParameter("Pitch", -24, 24, () => PitchSemitones, v => PitchSemitones = v, "0", "st") { Group = "Global" });
        list.Add(new FloatParameter("Glide", 0, 2000, () => GlideMs, v => GlideMs = v, "0", "ms") { Group = "Global" });
        list.Add(new FloatParameter("Gain", 0, 1, () => Gain, v => Gain = v, "0.00") { Group = "Global" });
        list.Add(new FloatParameter("Pan", -1, 1, () => Pan, v => Pan = v, "0.00") { Group = "Global" });
        list.Add(new FloatParameter("Vel→Index", 0, 1, () => VelocityToIndex, v => VelocityToIndex = v, "0.00") { Group = "Global" });

        return list;
    }

    public override IInstrument Clone()
    {
        var c = new FmSynthInstrument();
        CopyStateTo(c);
        return c;
    }

    public void CopyStateTo(FmSynthInstrument c)
    {
        for (var i = 0; i < OpCount; i++) Operators[i].CopyTo(c.Operators[i]);
        for (var i = 0; i < OpCount; i++)
        {
            for (var j = 0; j < OpCount; j++)
                c.Matrix[i, j] = Matrix[i, j];
            c.NoiseToOp[i] = NoiseToOp[i];
        }

        c.NoiseLevel = NoiseLevel;
        c.NoiseModLevel = NoiseModLevel;
        c.NoiseModEnabled = NoiseModEnabled;
        c.NoiseCutoff = NoiseCutoff;
        c.NoiseResonance = NoiseResonance;
        c.NoiseDrive = NoiseDrive;

        c.ModEnvAmount = ModEnvAmount;
        c.ModAttackSeconds = ModAttackSeconds;
        c.ModDecaySeconds = ModDecaySeconds;
        c.ModSustainLevel = ModSustainLevel;
        c.ModReleaseSeconds = ModReleaseSeconds;

        c.FilterOn = FilterOn;
        c.FilterModeIndex = FilterModeIndex;
        c.Cutoff = Cutoff;
        c.Resonance = Resonance;
        c.FilterEnvAmount = FilterEnvAmount;
        c.FilterAttackSeconds = FilterAttackSeconds;
        c.FilterDecaySeconds = FilterDecaySeconds;
        c.FilterSustainLevel = FilterSustainLevel;
        c.FilterReleaseSeconds = FilterReleaseSeconds;

        c.AttackSeconds = AttackSeconds;
        c.DecaySeconds = DecaySeconds;
        c.SustainLevel = SustainLevel;
        c.ReleaseSeconds = ReleaseSeconds;
        c.AmpVelocitySens = AmpVelocitySens;

        c.PitchSemitones = PitchSemitones;
        c.GlideMs = GlideMs;
        c.Gain = Gain;
        c.Pan = Pan;
        c.VelocityToIndex = VelocityToIndex;
    }

    private static readonly string[] PresetNamesList =
    {
        "Init", "Glass Bells", "Warm Pad", "Electric Piano", "Metallic Hit",
        "Bass Growl", "Crystal Pluck", "Soft Clarinet", "Bright Stab", "DX Stack"
    };

    public IReadOnlyList<string> PresetNames => PresetNamesList;

    public void LoadPreset(int index)
    {
        switch (index)
        {
            case 1: ApplyGlassBells(); break;
            case 2: ApplyWarmPad(); break;
            case 3: ApplyElectricPiano(); break;
            case 4: ApplyMetallicHit(); break;
            case 5: ApplyBassGrowl(); break;
            case 6: ApplyCrystalPluck(); break;
            case 7: ApplySoftClarinet(); break;
            case 8: ApplyBrightStab(); break;
            case 9: ApplyDxStack(); break;
            default: Reset(); break;
        }
    }

    /// <summary>Classic 2-op routing: modulator <paramref name="modOp"/> → carrier op 1.</summary>
    public void SetClassic2Op(int modOp, double ratio, double matrix999, double modLevel = 1.0)
    {
        Reset();
        Operators[0].Ratio = 1.0;
        Operators[0].Level = 0.72;
        var m = Math.Clamp(modOp, 1, 4) - 1;
        Operators[m].Ratio = ratio;
        Operators[m].ModLevel = modLevel;
        Operators[m].ModEnabled = true;
        Matrix[m, 0] = matrix999 / 999.0;
    }

    public void Reset()
    {
        for (var i = 0; i < OpCount; i++)
        {
            Operators[i].Ratio = 1.0;
            Operators[i].OffsetHz = 0;
            Operators[i].WaveIndex = 0;
            Operators[i].ModLevel = 1.0;
            Operators[i].ModEnabled = true;
            Operators[i].Level = i == 0 ? 0.65 : 0.0;
            Operators[i].Pan = 0;
        }

        Array.Clear(Matrix, 0, Matrix.Length);
        Matrix[1, 0] = 0.35;
        Operators[1].Ratio = 2.0;
        Operators[1].Level = 0;

        Array.Clear(NoiseToOp, 0, NoiseToOp.Length);

        NoiseLevel = 0;
        NoiseModLevel = 1.0;
        NoiseModEnabled = true;
        NoiseCutoff = 8000;
        NoiseResonance = 0.7;
        NoiseDrive = 0;

        ModEnvAmount = 1.0;
        ModAttackSeconds = 0.005;
        ModDecaySeconds = 0.3;
        ModSustainLevel = 0;
        ModReleaseSeconds = 0.4;

        FilterOn = true;
        FilterModeIndex = 0;
        Cutoff = 12000;
        Resonance = 0.8;
        FilterEnvAmount = 0.25;
        FilterAttackSeconds = 0.005;
        FilterDecaySeconds = 0.25;
        FilterSustainLevel = 0.2;
        FilterReleaseSeconds = 0.3;

        AttackSeconds = 0.005;
        DecaySeconds = 0.12;
        SustainLevel = 0.7;
        ReleaseSeconds = 0.25;
        AmpVelocitySens = 1.0;

        PitchSemitones = 0;
        GlideMs = 0;
        Gain = 0.75;
        Pan = 0;
        VelocityToIndex = 0.65;
    }

    private void ApplyGlassBells()
    {
        Reset();
        Operators[0].Level = 0.55;
        Operators[3].Ratio = 2.0;
        Matrix[3, 0] = 0.55;
        ModEnvAmount = 0.95;
        ModAttackSeconds = 0.001;
        ModDecaySeconds = 2.5;
        ModSustainLevel = 0;
        ModReleaseSeconds = 2.0;
        AttackSeconds = 0.003;
        DecaySeconds = 1.1;
        SustainLevel = 0;
        ReleaseSeconds = 1.6;
        FilterOn = true;
        Cutoff = 9000;
        FilterEnvAmount = 0.15;
    }

    private void ApplyWarmPad()
    {
        SetClassic2Op(2, 1.0, 180);
        Operators[0].WaveIndex = 0;
        Operators[1].WaveIndex = 0;
        Operators[2].Ratio = 1.01;
        Operators[2].Level = 0.22;
        Operators[2].Pan = -0.35;
        Operators[3].Ratio = 0.99;
        Operators[3].Level = 0.22;
        Operators[3].Pan = 0.35;
        Matrix[2, 0] = 0.08;
        Matrix[3, 0] = 0.08;
        AttackSeconds = 0.4;
        DecaySeconds = 0.6;
        SustainLevel = 0.65;
        ReleaseSeconds = 2.4;
        Cutoff = 4500;
        FilterEnvAmount = 0.4;
    }

    private void ApplyElectricPiano()
    {
        SetClassic2Op(2, 2.0, 420);
        Operators[1].Ratio = 1.0;
        Operators[1].Level = 0.18;
        Matrix[1, 0] = 0.25;
        AttackSeconds = 0.002;
        DecaySeconds = 1.4;
        SustainLevel = 0.15;
        ReleaseSeconds = 0.6;
        VelocityToIndex = 0.85;
    }

    private void ApplyMetallicHit()
    {
        SetClassic2Op(2, 3.5, 650);
        Operators[0].WaveIndex = 1;
        AttackSeconds = 0.001;
        DecaySeconds = 0.35;
        SustainLevel = 0;
        ReleaseSeconds = 0.4;
        FilterOn = true;
        Cutoff = 6000;
        Resonance = 1.2;
    }

    private void ApplyBassGrowl()
    {
        SetClassic2Op(2, 1.0, 520);
        Operators[0].WaveIndex = 2;
        Operators[2].Ratio = 0.5;
        Operators[2].Level = 0.35;
        Matrix[2, 0] = 0.2;
        AttackSeconds = 0.005;
        DecaySeconds = 0.3;
        SustainLevel = 0.4;
        ReleaseSeconds = 0.15;
        Cutoff = 900;
        FilterEnvAmount = 0.55;
        Gain = 0.85;
    }

    private void ApplyCrystalPluck()
    {
        SetClassic2Op(2, 2.0, 480);
        ModEnvAmount = 0.9;
        ModDecaySeconds = 0.5;
        AttackSeconds = 0.001;
        DecaySeconds = 0.4;
        SustainLevel = 0;
        ReleaseSeconds = 0.35;
        FilterEnvAmount = 0.65;
        FilterDecaySeconds = 0.35;
    }

    private void ApplySoftClarinet()
    {
        SetClassic2Op(2, 1.0, 220);
        Operators[0].WaveIndex = 0;
        Operators[1].WaveIndex = 0;
        Matrix[1, 0] = 0.35;
        AttackSeconds = 0.08;
        DecaySeconds = 0.15;
        SustainLevel = 0.85;
        ReleaseSeconds = 0.2;
        Cutoff = 2800;
    }

    private void ApplyBrightStab()
    {
        SetClassic2Op(2, 4.0, 720);
        Operators[0].WaveIndex = 2;
        AttackSeconds = 0.001;
        DecaySeconds = 0.12;
        SustainLevel = 0;
        ReleaseSeconds = 0.08;
        VelocityToIndex = 1.0;
    }

    private void ApplyDxStack()
    {
        Reset();
        Operators[0].Level = 0.45;
        Operators[1].Ratio = 2.0;
        Operators[1].Level = 0.2;
        Operators[2].Ratio = 3.0;
        Operators[2].Level = 0.15;
        Operators[3].Ratio = 4.0;
        Operators[3].Level = 0.12;
        Matrix[1, 0] = 0.45;
        Matrix[2, 0] = 0.3;
        Matrix[3, 0] = 0.22;
        Matrix[2, 1] = 0.15;
        NoiseLevel = 0.04;
        NoiseToOp[0] = 0.08;
        FilterOn = true;
        Cutoff = 7500;
    }

    protected override Voice CreateVoice() => new FmVoice(this);

    internal static float WaveAt(OscWave wave, double phase, double inc)
    {
        var p = phase - Math.Floor(phase);
        var dt = inc <= 0 ? 0.0 : Math.Min(inc, 0.5);
        double value;
        switch (wave)
        {
            case OscWave.Triangle:
                value = 1.0 - 4.0 * Math.Abs(p - 0.5);
                break;
            case OscWave.Saw:
                value = 2.0 * p - 1.0 - PolyBlep(p, dt);
                break;
            case OscWave.Square:
                value = p < 0.5 ? 1.0 : -1.0;
                value += PolyBlep(p, dt);
                var pHalf = p + 0.5;
                pHalf -= Math.Floor(pHalf);
                value -= PolyBlep(pHalf, dt);
                break;
            default:
                value = Math.Sin(p * TwoPi);
                break;
        }

        return (float)value;
    }

    private static double PolyBlep(double t, double dt)
    {
        if (dt <= 0.0) return 0.0;
        if (t < dt) { t /= dt; return t + t - t * t - 1.0; }
        if (t > 1.0 - dt) { t = (t - 1.0) / dt; return t * t + t + t + 1.0; }
        return 0.0;
    }

    private sealed class FmVoice : Voice
    {
        private const float VoiceGain = 0.2f;

        private readonly FmSynthInstrument _inst;
        private readonly AdsrEnvelope _amp = new();
        private readonly AdsrEnvelope _modEnv = new();
        private readonly AdsrEnvelope _filtEnv = new();
        private readonly double[] _phase = new double[OpCount];
        private readonly float[] _prevOp = new float[OpCount];
        private readonly Biquad _filterL = new();
        private readonly Biquad _filterR = new();
        private Biquad _noiseFilter;
        private FastRandom _noiseRng = new(0xF001u);
        private readonly float[] _opOut = new float[OpCount];

        private float _velocity;
        private double _glideHz;
        private double _targetHz;

        public FmVoice(FmSynthInstrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            Array.Clear(_phase, 0, OpCount);
            Array.Clear(_prevOp, 0, OpCount);
            _filterL.Reset();
            _filterR.Reset();
            _noiseFilter.Reset();
            _noiseRng = new FastRandom((uint)(0xBEEFu + (uint)midiNote * 7919u));

            var sr = format.SampleRate;
            _targetHz = NoteFrequency(midiNote);
            _glideHz = _targetHz;

            _amp.SetSampleRate(sr);
            _amp.AttackSeconds = _inst.AttackSeconds;
            _amp.DecaySeconds = _inst.DecaySeconds;
            _amp.SustainLevel = _inst.SustainLevel;
            _amp.ReleaseSeconds = _inst.ReleaseSeconds;
            _amp.Gate();

            _modEnv.SetSampleRate(sr);
            _modEnv.AttackSeconds = _inst.ModAttackSeconds;
            _modEnv.DecaySeconds = _inst.ModDecaySeconds;
            _modEnv.SustainLevel = _inst.ModSustainLevel;
            _modEnv.ReleaseSeconds = _inst.ModReleaseSeconds;
            _modEnv.Gate();

            _filtEnv.SetSampleRate(sr);
            _filtEnv.AttackSeconds = _inst.FilterAttackSeconds;
            _filtEnv.DecaySeconds = _inst.FilterDecaySeconds;
            _filtEnv.SustainLevel = _inst.FilterSustainLevel;
            _filtEnv.ReleaseSeconds = _inst.FilterReleaseSeconds;
            _filtEnv.Gate();
        }

        public override void Release()
        {
            _amp.Release();
            _modEnv.Release();
            _filtEnv.Release();
        }

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var sr = Format.SampleRate;
            var maxCut = sr * 0.45;
            var stereo = channels >= 2;

            var velAmp = _velocity * (float)(0.2 + 0.8 * _inst.AmpVelocitySens);
            var velIndex = 1.0 + _inst.VelocityToIndex * (_velocity - 0.5) * 2.0;
            var masterGain = (float)(_inst.Gain * VoiceGain) * velAmp;
            var globalPan = Math.Clamp(_inst.Pan, -1, 1);
            var glideCoeff = _inst.GlideMs <= 0 ? 1.0 : 1.0 - Math.Exp(-1.0 / (sr * _inst.GlideMs / 1000.0));

            for (var frame = 0; frame < frames; frame++)
            {
                _targetHz = NoteFrequency(Note);
                _glideHz += (_targetHz - _glideHz) * glideCoeff;
                var baseHz = _glideHz;

                var modEnv = _modEnv.Process() * _inst.ModEnvAmount;
                var modScale = modEnv * velIndex * 4.0;

                var noiseRaw = _noiseRng.NextBipolar();
                var noiseDrive = 1.0 + _inst.NoiseDrive * 8.0;
                noiseRaw = (float)Math.Tanh(noiseRaw * noiseDrive);
                var nCut = Math.Clamp(_inst.NoiseCutoff, 80, maxCut);
                var nCoeffs = BiquadCoefficients.Compute(FilterMode.LowPass, nCut, _inst.NoiseResonance, sr);
                var noiseFilt = (float)_noiseFilter.Process(in nCoeffs, noiseRaw);
                var noiseSample = noiseFilt * (float)_inst.NoiseLevel;

                for (var iter = 0; iter < 2; iter++)
                {
                    for (var dst = 0; dst < OpCount; dst++)
                    {
                        var mod = 0.0;
                        for (var src = 0; src < OpCount; src++)
                        {
                            var op = _inst.Operators[src];
                            if (!op.ModEnabled) continue;
                            var amt = _inst.Matrix[src, dst];
                            if (amt <= 0) continue;
                            mod += amt * _prevOp[src] * op.ModLevel;
                        }

                        if (_inst.NoiseModEnabled)
                            mod += _inst.NoiseToOp[dst] * noiseSample * _inst.NoiseModLevel;

                        var opSettings = _inst.Operators[dst];
                        var hz = baseHz * opSettings.Ratio + opSettings.OffsetHz;
                        if (hz < 0.1) hz = 0.1;
                        var inc = hz / sr;
                        var wave = (OscWave)Math.Clamp(opSettings.WaveIndex, 0, 3);
                        var phaseMod = mod * modScale;
                        _opOut[dst] = WaveAt(wave, _phase[dst] + phaseMod, inc) * (float)opSettings.Level;
                        _phase[dst] += inc;
                        if (_phase[dst] >= 1.0) _phase[dst] -= 1.0;
                    }

                    for (var i = 0; i < OpCount; i++) _prevOp[i] = _opOut[i];
                }

                float mixL = 0, mixR = 0;
                for (var i = 0; i < OpCount; i++)
                {
                    var p = Math.Clamp(_inst.Operators[i].Pan, -1, 1);
                    var panL = (float)Math.Sqrt(0.5 * (1.0 - p));
                    var panR = (float)Math.Sqrt(0.5 * (1.0 + p));
                    mixL += _opOut[i] * panL;
                    mixR += _opOut[i] * panR;
                }

                if (!stereo) mixL = (mixL + mixR) * 0.5f;

                if (_inst.FilterOn)
                {
                    var fEnv = _filtEnv.Process();
                    var cutoff = Math.Clamp(_inst.Cutoff + fEnv * _inst.FilterEnvAmount * 8000, 80, maxCut);
                    var mode = _inst.FilterModeIndex switch
                    {
                        1 => FilterMode.BandPass,
                        2 => FilterMode.HighPass,
                        _ => FilterMode.LowPass
                    };
                    var coeffs = BiquadCoefficients.Compute(mode, cutoff, _inst.Resonance, sr);
                    mixL = (float)_filterL.Process(in coeffs, mixL);
                    mixR = (float)_filterR.Process(in coeffs, mixR);
                }

                var amp = _amp.Process() * masterGain;
                mixL *= amp;
                mixR *= amp;

                if (stereo)
                {
                    var gPanL = (float)Math.Sqrt(0.5 * (1.0 - globalPan));
                    var gPanR = (float)Math.Sqrt(0.5 * (1.0 + globalPan));
                    var idx = frame * channels;
                    buffer[idx] += mixL * gPanL;
                    buffer[idx + 1] += mixR * gPanR;
                }
                else
                {
                    buffer[frame] += mixL;
                }

                if (!_amp.IsActive)
                {
                    IsActive = false;
                    return;
                }
            }
        }

        private double NoteFrequency(int midiNote)
            => MusicalMath.NoteToFrequency(midiNote + (int)Math.Round(_inst.PitchSemitones));
    }
}

/// <summary>Editable settings for one FM operator.</summary>
public sealed class FmOperatorSettings
{
    public double Ratio { get; set; } = 1.0;
    public double OffsetHz { get; set; }
    public int WaveIndex { get; set; }
    public double ModLevel { get; set; } = 1.0;
    public bool ModEnabled { get; set; } = true;
    public double Level { get; set; }
    public double Pan { get; set; }

    public void CopyTo(FmOperatorSettings other)
    {
        other.Ratio = Ratio;
        other.OffsetHz = OffsetHz;
        other.WaveIndex = WaveIndex;
        other.ModLevel = ModLevel;
        other.ModEnabled = ModEnabled;
        other.Level = Level;
        other.Pan = Pan;
    }
}
