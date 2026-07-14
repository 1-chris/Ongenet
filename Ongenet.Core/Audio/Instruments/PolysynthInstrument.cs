using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Dual-oscillator subtractive synth: shape-blended oscillators with blend operators,
/// multimode filter, noise, and amp/filter envelopes. Built from reusable DSP primitives.
/// </summary>
public sealed class PolysynthInstrument : PolyphonicInstrument
{
    public const string TypeId = "polysynth";

    private static readonly string[] BlendNames = { "Mix", "Neg", "Wipe", "AM", "Sign", "Max" };
    private static readonly string[] FilterModes = { "LP", "BP", "HP", "Notch" };

    protected override string GetTypeId() => TypeId;
    public override string Name => "Polysynth";

    private Parameter[]? _parameters;

    public int BlendOp { get; set; }
    public double OscMix { get; set; } = 0.5;
    public double Noise { get; set; }
    public double FilterFm { get; set; }
    public int FilterMode { get; set; }
    public double Cutoff { get; set; } = 4000;
    public double Resonance { get; set; } = 1.0;
    public double FilterEnvAmt { get; set; } = 0.35;

    public double Attack { get; set; } = 0.005;
    public double Decay { get; set; } = 0.15;
    public double Sustain { get; set; } = 0.7;
    public double Release { get; set; } = 0.25;
    public double FAttack { get; set; } = 0.003;
    public double FDecay { get; set; } = 0.2;
    public double FSustain { get; set; } = 0.3;
    public double FRelease { get; set; } = 0.2;

    public PolysynthInstrument() : base(polyphony: 16) { }

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Blend", BlendNames, () => BlendOp, i => BlendOp = i) { Group = "Blend" },
        new FloatParameter("1/2", 0, 1, () => OscMix, v => OscMix = v, "0.00") { Group = "Blend" },
        new FloatParameter("Noise", 0, 1, () => Noise, v => Noise = v, "0.00") { Group = "Blend" },
        new FloatParameter("Filter FM", 0, 1, () => FilterFm, v => FilterFm = v, "0.00") { Group = "Blend" },
        new ChoiceParameter("Mode", FilterModes, () => FilterMode, i => FilterMode = i) { Group = "Filter" },
        new FloatParameter("Cutoff", 80, 18000, () => Cutoff, v => Cutoff = v, "0", "Hz", skew: 3.0) { Group = "Filter" },
        new FloatParameter("Reso", 0.5, 12, () => Resonance, v => Resonance = v, "0.0") { Group = "Filter" },
        new FloatParameter("Env Amt", 0, 1, () => FilterEnvAmt, v => FilterEnvAmt = v, "0.00") { Group = "Filter" },
        new FloatParameter("Attack", 0.001, 2, () => Attack, v => Attack = v, "0.000", "s") { Group = "Amp Env" },
        new FloatParameter("Decay", 0.001, 2, () => Decay, v => Decay = v, "0.000", "s") { Group = "Amp Env" },
        new FloatParameter("Sustain", 0, 1, () => Sustain, v => Sustain = v, "0.00") { Group = "Amp Env" },
        new FloatParameter("Release", 0.001, 3, () => Release, v => Release = v, "0.000", "s") { Group = "Amp Env" },
        new FloatParameter("Attack", 0.001, 2, () => FAttack, v => FAttack = v, "0.000", "s") { Group = "Filter Env" },
        new FloatParameter("Decay", 0.001, 2, () => FDecay, v => FDecay = v, "0.000", "s") { Group = "Filter Env" },
        new FloatParameter("Sustain", 0, 1, () => FSustain, v => FSustain = v, "0.00") { Group = "Filter Env" },
        new FloatParameter("Release", 0.001, 3, () => FRelease, v => FRelease = v, "0.000", "s") { Group = "Filter Env" }
    };

    protected override Voice CreateVoice() => new PolysynthVoice(this);

    public override IInstrument Clone() => new PolysynthInstrument
    {
        BlendOp = BlendOp, OscMix = OscMix, Noise = Noise, FilterFm = FilterFm,
        FilterMode = FilterMode, Cutoff = Cutoff, Resonance = Resonance, FilterEnvAmt = FilterEnvAmt,
        Attack = Attack, Decay = Decay, Sustain = Sustain, Release = Release,
        FAttack = FAttack, FDecay = FDecay, FSustain = FSustain, FRelease = FRelease
    };

    private sealed class PolysynthVoice : Voice
    {
        private const float VoiceGain = 0.22f;
        private readonly PolysynthInstrument _inst;
        private readonly PolysynthOscillator _osc1 = new();
        private readonly PolysynthOscillator _osc2 = new();
        private readonly WaveOscillator _noise = new();
        private readonly Biquad _filter = new();
        private readonly AdsrEnvelope _amp = new();
        private readonly AdsrEnvelope _fenv = new();
        private float _velocity;
        private double _filterFmPhase;

        public PolysynthVoice(PolysynthInstrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            var sr = format.SampleRate;
            var hz = MusicalMath.NoteToFrequency(midiNote);
            _osc1.SetSampleRate(sr);
            _osc2.SetSampleRate(sr);
            _noise.SetSampleRate(sr);
            _noise.Wave = OscWave.Noise;
            _noise.SeedNoise((uint)(midiNote * 7919 + 17));
            _osc1.SetBaseFrequency(hz);
            _osc2.SetBaseFrequency(hz);
            _osc1.Reset();
            _osc2.Reset();
            _osc2.PitchSemitones = 7;
            _filterFmPhase = 0;
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
            var blend = (PolysynthBlendOp)Math.Clamp(_inst.BlendOp, 0, 5);
            var mix = (float)_inst.OscMix;
            var mode = MapFilterMode(_inst.FilterMode);

            for (var frame = 0; frame < frames; frame++)
            {
                var o1 = _osc1.Process();
                var o2 = _osc2.Process();
                var gen = PolysynthOscillator.Blend(o1, o2, blend, mix);
                gen = gen * (1f - (float)_inst.Noise) + _noise.Next() * (float)_inst.Noise;

                _filterFmPhase += 220.0 / sr;
                if (_filterFmPhase >= 1) _filterFmPhase -= 1;
                var fm = Math.Sin(_filterFmPhase * Math.PI * 2) * _inst.FilterFm * 2000;
                var fEnv = _fenv.Process();
                var cutoff = AudioMath.Clamp(_inst.Cutoff + fEnv * _inst.FilterEnvAmt * 8000 + fm, 80, maxCut);
                var coeffs = BiquadCoefficients.Compute(mode, cutoff, _inst.Resonance, sr);
                var filtered = (float)_filter.Process(coeffs, gen);
                var sample = filtered * _amp.Process() * _velocity * VoiceGain;

                var bi = frame * channels;
                for (var c = 0; c < channels; c++) buffer[bi + c] += sample;

                if (!_amp.IsActive) { IsActive = false; return; }
            }
        }

        private static Effects.FilterMode MapFilterMode(int i) => i switch
        {
            1 => Effects.FilterMode.BandPass,
            2 => Effects.FilterMode.HighPass,
            3 => Effects.FilterMode.Notch,
            _ => Effects.FilterMode.LowPass
        };
    }
}
