using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A four-operator phase-distortion synth inspired by Casio CZ voices. Each operator is a
/// <see cref="PhaseDistortionOsc"/> with its own harmonic ratio and distortion amount, summed and
/// shaped by an optional resonant low-pass and an ADSR amp envelope.
/// </summary>
public sealed class Phase4Instrument : PolyphonicInstrument
{
    public const string TypeId = "phase4";

    protected override string GetTypeId() => TypeId;

    private Parameter[]? _parameters;

    public override string Name => "Phase-4";

    public double Ratio1 { get; set; } = 1.0;
    public double Ratio2 { get; set; } = 2.0;
    public double Ratio3 { get; set; } = 3.0;
    public double Ratio4 { get; set; } = 4.0;
    public double Amount1 { get; set; } = 0.2;
    public double Amount2 { get; set; } = 0.45;
    public double Amount3 { get; set; } = 0.6;
    public double Amount4 { get; set; } = 0.35;

    public bool FilterOn { get; set; } = true;
    public double Cutoff { get; set; } = 8000;
    public double Resonance { get; set; } = 0.8;

    public double AttackSeconds { get; set; } = 0.005;
    public double DecaySeconds { get; set; } = 0.12;
    public double SustainLevel { get; set; } = 0.65;
    public double ReleaseSeconds { get; set; } = 0.25;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Ratio", 0.25, 16, () => Ratio1, v => Ratio1 = v, "0.00") { Group = "Op 1" },
        new FloatParameter("Amount", 0, 1, () => Amount1, v => Amount1 = v, "0.00") { Group = "Op 1" },
        new FloatParameter("Ratio", 0.25, 16, () => Ratio2, v => Ratio2 = v, "0.00") { Group = "Op 2" },
        new FloatParameter("Amount", 0, 1, () => Amount2, v => Amount2 = v, "0.00") { Group = "Op 2" },
        new FloatParameter("Ratio", 0.25, 16, () => Ratio3, v => Ratio3 = v, "0.00") { Group = "Op 3" },
        new FloatParameter("Amount", 0, 1, () => Amount3, v => Amount3 = v, "0.00") { Group = "Op 3" },
        new FloatParameter("Ratio", 0.25, 16, () => Ratio4, v => Ratio4 = v, "0.00") { Group = "Op 4" },
        new FloatParameter("Amount", 0, 1, () => Amount4, v => Amount4 = v, "0.00") { Group = "Op 4" },

        new BoolParameter("On", () => FilterOn, v => FilterOn = v) { Group = "Filter" },
        new FloatParameter("Cutoff", 80, 18000, () => Cutoff, v => Cutoff = v, "0", "Hz", skew: 3.0) { Group = "Filter" },
        new FloatParameter("Reso", 0.5, 12, () => Resonance, v => Resonance = v, "0.0") { Group = "Filter" },

        new FloatParameter("Attack", 0.001, 2, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Decay", 0.001, 2, () => DecaySeconds, v => DecaySeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Sustain", 0, 1, () => SustainLevel, v => SustainLevel = v, "0.00") { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 3, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s") { Group = "Amp Envelope" }
    };

    protected override Voice CreateVoice() => new Phase4Voice(this);

    public override IInstrument Clone() => new Phase4Instrument
    {
        Ratio1 = Ratio1, Ratio2 = Ratio2, Ratio3 = Ratio3, Ratio4 = Ratio4,
        Amount1 = Amount1, Amount2 = Amount2, Amount3 = Amount3, Amount4 = Amount4,
        FilterOn = FilterOn, Cutoff = Cutoff, Resonance = Resonance,
        AttackSeconds = AttackSeconds, DecaySeconds = DecaySeconds,
        SustainLevel = SustainLevel, ReleaseSeconds = ReleaseSeconds
    };

    private sealed class Phase4Voice : Voice
    {
        private const float VoiceGain = 0.18f;

        private readonly Phase4Instrument _inst;
        private readonly PhaseDistortionOsc[] _ops = { new(), new(), new(), new() };
        private readonly AdsrEnvelope _envelope = new();
        private Biquad _filter;
        private double _baseHz;
        private float _velocity;

        public Phase4Voice(Phase4Instrument inst) => _inst = inst;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            _baseHz = MusicalMath.NoteToFrequency(midiNote);

            foreach (var op in _ops) op.Reset();

            _filter.Reset();
            _envelope.SetSampleRate(format.SampleRate);
            _envelope.AttackSeconds = _inst.AttackSeconds;
            _envelope.DecaySeconds = _inst.DecaySeconds;
            _envelope.SustainLevel = _inst.SustainLevel;
            _envelope.ReleaseSeconds = _inst.ReleaseSeconds;
            _envelope.Gate();
        }

        public override void Release() => _envelope.Release();

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var sr = Format.SampleRate;
            var filterOn = _inst.FilterOn;
            BiquadCoefficients? coeffs = null;
            if (filterOn)
                coeffs = BiquadCoefficients.Compute(FilterMode.LowPass, _inst.Cutoff, _inst.Resonance, sr);

            for (var frame = 0; frame < frames; frame++)
            {
                var sum =
                    _ops[0].Process(_baseHz * _inst.Ratio1, _inst.Amount1, sr) +
                    _ops[1].Process(_baseHz * _inst.Ratio2, _inst.Amount2, sr) +
                    _ops[2].Process(_baseHz * _inst.Ratio3, _inst.Amount3, sr) +
                    _ops[3].Process(_baseHz * _inst.Ratio4, _inst.Amount4, sr);

                var sample = (float)(sum * 0.25) * _envelope.Process() * _velocity * VoiceGain;
                if (filterOn && coeffs.HasValue)
                    sample = (float)_filter.Process(coeffs.Value, sample);

                var baseIndex = frame * channels;
                for (var ch = 0; ch < channels; ch++)
                    buffer[baseIndex + ch] += sample;

                if (!_envelope.IsActive)
                {
                    IsActive = false;
                    return;
                }
            }
        }
    }
}
