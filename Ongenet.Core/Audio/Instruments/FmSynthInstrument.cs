using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A 2-operator FM synth: a sine carrier whose phase is modulated by a sine modulator at
/// <see cref="ModRatio"/> × the note frequency, with depth <see cref="ModIndex"/>, shaped by an
/// ADSR envelope. The second built-in instrument, built on the same framework as the oscillator.
/// </summary>
public sealed class FmSynthInstrument : PolyphonicInstrument
{
    public const string TypeId = "fmsynth";

    protected override string GetTypeId() => TypeId;

    public double ModRatio { get; set; } = 2.0;
    public double ModIndex { get; set; } = 2.0;
    public double AttackSeconds { get; set; } = 0.005;
    public double DecaySeconds { get; set; } = 0.12;
    public double SustainLevel { get; set; } = 0.7;
    public double ReleaseSeconds { get; set; } = 0.25;

    public override string Name => "FM Synth";

    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Ratio", 0.5, 8.0, () => ModRatio, v => ModRatio = v, "0.0") { Group = "FM" },
        new FloatParameter("Mod Index", 0.0, 12.0, () => ModIndex, v => ModIndex = v, "0.0") { Group = "FM" },
        new FloatParameter("Attack", 0.001, 2.0, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Decay", 0.001, 2.0, () => DecaySeconds, v => DecaySeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Sustain", 0.0, 1.0, () => SustainLevel, v => SustainLevel = v) { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 3.0, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s") { Group = "Amp Envelope" }
    };

    public override IInstrument Clone() => new FmSynthInstrument
    {
        ModRatio = ModRatio,
        ModIndex = ModIndex,
        AttackSeconds = AttackSeconds,
        DecaySeconds = DecaySeconds,
        SustainLevel = SustainLevel,
        ReleaseSeconds = ReleaseSeconds
    };

    protected override Voice CreateVoice() => new FmVoice(this);

    private sealed class FmVoice : Voice
    {
        private const float VoiceGain = 0.22f;
        private const double TwoPi = 2.0 * Math.PI;

        private readonly FmSynthInstrument _instrument;
        private readonly AdsrEnvelope _envelope = new();
        private double _carrierPhase;   // cycles, [0,1)
        private double _modPhase;
        private double _carrierInc;
        private double _modInc;
        private float _velocity;

        public FmVoice(FmSynthInstrument instrument) => _instrument = instrument;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            _carrierPhase = 0;
            _modPhase = 0;

            var freq = MusicalMath.NoteToFrequency(midiNote);
            _carrierInc = freq / format.SampleRate;
            _modInc = freq * _instrument.ModRatio / format.SampleRate;

            _envelope.SetSampleRate(format.SampleRate);
            _envelope.AttackSeconds = _instrument.AttackSeconds;
            _envelope.DecaySeconds = _instrument.DecaySeconds;
            _envelope.SustainLevel = _instrument.SustainLevel;
            _envelope.ReleaseSeconds = _instrument.ReleaseSeconds;
            _envelope.Gate();
        }

        public override void Release() => _envelope.Release();

        public override void Render(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;

            for (var frame = 0; frame < frames; frame++)
            {
                // Pick up live ratio so re-tuned voices follow (index is read per-sample below).
                _modInc = MusicalMath.NoteToFrequency(Note) * _instrument.ModRatio / Format.SampleRate;

                var modulator = Math.Sin(_modPhase * TwoPi);
                var sample = (float)Math.Sin((_carrierPhase + _instrument.ModIndex * modulator) * TwoPi)
                             * _envelope.Process() * _velocity * VoiceGain;

                _carrierPhase += _carrierInc;
                if (_carrierPhase >= 1.0) _carrierPhase -= 1.0;
                _modPhase += _modInc;
                if (_modPhase >= 1.0) _modPhase -= 1.0;

                var baseIndex = frame * channels;
                for (var c = 0; c < channels; c++) buffer[baseIndex + c] += sample;

                if (!_envelope.IsActive)
                {
                    IsActive = false;
                    return;
                }
            }
        }
    }
}
