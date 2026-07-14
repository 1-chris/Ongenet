using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Dual-oscillator subtractive synth: resonant filter with envelope + LFO
/// modulation, amp envelope. Same architecture as the Field Polymer patch, as a native instrument.
/// </summary>
public sealed class PolymerInstrument : PolyphonicInstrument
{
    public const string TypeId = "polymer";

    protected override string GetTypeId() => TypeId;
    public override string Name => "Polymer";

    private Parameter[]? _parameters;

    public int WaveA { get; set; } = 2;
    public int WaveB { get; set; } = 2;
    public double FineB { get; set; } = 14;
    public double LevelA { get; set; } = 0.65;
    public double LevelB { get; set; } = 0.55;
    public double Cutoff { get; set; } = 2400;
    public double Resonance { get; set; } = 1.2;
    public double FilterEnvAmt { get; set; } = 0.12;
    public double LfoRate { get; set; } = 0.35;
    public double LfoDepth { get; set; } = 0.25;

    public double Attack { get; set; } = 0.004;
    public double Decay { get; set; } = 0.2;
    public double Sustain { get; set; } = 0.65;
    public double Release { get; set; } = 0.25;
    public double FAttack { get; set; } = 0.002;
    public double FDecay { get; set; } = 0.35;
    public double FSustain { get; set; } = 0.2;
    public double FRelease { get; set; } = 0.2;

    public PolymerInstrument() : base(polyphony: 16) { }

    private static readonly string[] WaveNames = { "Sine", "Triangle", "Saw", "Square" };

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Wave", WaveNames, () => WaveA, i => WaveA = i) { Group = "Osc A" },
        new FloatParameter("Level", 0, 1, () => LevelA, v => LevelA = v, "0.00") { Group = "Osc A" },
        new ChoiceParameter("Wave", WaveNames, () => WaveB, i => WaveB = i) { Group = "Osc B" },
        new FloatParameter("Fine", -100, 100, () => FineB, v => FineB = v, "0", "ct") { Group = "Osc B" },
        new FloatParameter("Level", 0, 1, () => LevelB, v => LevelB = v, "0.00") { Group = "Osc B" },
        new FloatParameter("Cutoff", 80, 18000, () => Cutoff, v => Cutoff = v, "0", "Hz", skew: 3.0) { Group = "Filter" },
        new FloatParameter("Reso", 0.5, 12, () => Resonance, v => Resonance = v, "0.0") { Group = "Filter" },
        new FloatParameter("Env Amt", 0, 1, () => FilterEnvAmt, v => FilterEnvAmt = v, "0.00") { Group = "Filter" },
        new FloatParameter("LFO Rate", 0.05, 8, () => LfoRate, v => LfoRate = v, "0.##", "Hz") { Group = "Filter" },
        new FloatParameter("LFO Depth", 0, 1, () => LfoDepth, v => LfoDepth = v, "0.00") { Group = "Filter" },
        new FloatParameter("Attack", 0.001, 2, () => Attack, v => Attack = v, "0.000", "s") { Group = "Amp Env" },
        new FloatParameter("Decay", 0.001, 2, () => Decay, v => Decay = v, "0.000", "s") { Group = "Amp Env" },
        new FloatParameter("Sustain", 0, 1, () => Sustain, v => Sustain = v, "0.00") { Group = "Amp Env" },
        new FloatParameter("Release", 0.001, 3, () => Release, v => Release = v, "0.000", "s") { Group = "Amp Env" },
        new FloatParameter("Attack", 0.001, 2, () => FAttack, v => FAttack = v, "0.000", "s") { Group = "Filter Env" },
        new FloatParameter("Decay", 0.001, 2, () => FDecay, v => FDecay = v, "0.000", "s") { Group = "Filter Env" },
        new FloatParameter("Sustain", 0, 1, () => FSustain, v => FSustain = v, "0.00") { Group = "Filter Env" },
        new FloatParameter("Release", 0.001, 3, () => FRelease, v => FRelease = v, "0.000", "s") { Group = "Filter Env" }
    };

    protected override Voice CreateVoice() => new PolymerVoice(this);

    public override IInstrument Clone() => new PolymerInstrument
    {
        WaveA = WaveA, WaveB = WaveB, FineB = FineB, LevelA = LevelA, LevelB = LevelB,
        Cutoff = Cutoff, Resonance = Resonance, FilterEnvAmt = FilterEnvAmt,
        LfoRate = LfoRate, LfoDepth = LfoDepth,
        Attack = Attack, Decay = Decay, Sustain = Sustain, Release = Release,
        FAttack = FAttack, FDecay = FDecay, FSustain = FSustain, FRelease = FRelease
    };

    private sealed class PolymerVoice : Voice
    {
        private const float VoiceGain = 0.28f;
        private readonly PolymerInstrument _inst;
        private readonly WaveOscillator _oscA = new();
        private readonly WaveOscillator _oscB = new();
        private readonly Biquad _filter = new();
        private readonly AdsrEnvelope _amp = new();
        private readonly AdsrEnvelope _fenv = new();
        private readonly Lfo _lfo = new();
        private float _velocity;

        public PolymerVoice(PolymerInstrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            var sr = format.SampleRate;
            var hz = MusicalMath.NoteToFrequency(midiNote);
            _oscA.SetSampleRate(sr);
            _oscB.SetSampleRate(sr);
            _oscA.Wave = (OscWave)Math.Clamp(_inst.WaveA, 0, 3);
            _oscB.Wave = (OscWave)Math.Clamp(_inst.WaveB, 0, 3);
            _oscA.SetFrequency(hz);
            _oscB.SetFrequency(hz * Math.Pow(2, _inst.FineB / 1200.0));
            _oscA.ResetPhase();
            _oscB.ResetPhase();
            _lfo.Reset();
            _filter.Reset();

            _amp.SetSampleRate(sr);
            _amp.AttackSeconds = _inst.Attack;
            _amp.DecaySeconds = _inst.Decay;
            _amp.SustainLevel = _inst.Sustain;
            _amp.ReleaseSeconds = _inst.Release;
            _amp.Gate();

            _fenv.SetSampleRate(sr);
            _fenv.AttackSeconds = _inst.FAttack;
            _fenv.DecaySeconds = _inst.FDecay;
            _fenv.SustainLevel = _inst.FSustain;
            _fenv.ReleaseSeconds = _inst.FRelease;
            _fenv.Gate();
        }

        public override void Release()
        {
            _amp.Release();
            _fenv.Release();
        }

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var sr = Format.SampleRate;
            var maxCut = sr * 0.45;
            _lfo.SetRate(_inst.LfoRate, sr);

            for (var frame = 0; frame < frames; frame++)
            {
                var gen = _oscA.Next() * (float)_inst.LevelA + _oscB.Next() * (float)_inst.LevelB;
                var lfo = _lfo.Next() * 0.5f + 0.5f;
                var fEnv = _fenv.Process();
                var cutoff = AudioMath.Clamp(_inst.Cutoff + fEnv * _inst.FilterEnvAmt * 6000 + lfo * _inst.LfoDepth * 2000, 80, maxCut);
                var coeffs = BiquadCoefficients.Compute(FilterMode.LowPass, cutoff, _inst.Resonance, sr);
                var filtered = (float)_filter.Process(coeffs, gen);
                var sample = filtered * _amp.Process() * _velocity * VoiceGain;

                var bi = frame * channels;
                for (var c = 0; c < channels; c++) buffer[bi + c] += sample;

                if (!_amp.IsActive) { IsActive = false; return; }
            }
        }
    }
}
