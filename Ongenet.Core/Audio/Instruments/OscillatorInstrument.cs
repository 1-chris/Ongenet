using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// The first built-in instrument: a polyphonic synth where every voice is a single
/// <see cref="Oscillator"/> shaped by an <see cref="AdsrEnvelope"/>. The waveform can be
/// switched live (sine / sawtooth / square) and is picked up by sounding voices immediately.
/// </summary>
public sealed class OscillatorInstrument : PolyphonicInstrument
{
    /// <summary>Registry id for this instrument type.</summary>
    public const string TypeId = "oscillator";

    protected override string GetTypeId() => TypeId;

    /// <summary>Current waveform. Read live by every voice while rendering.</summary>
    public Waveform Waveform { get; set; } = Waveform.Sawtooth;

    // Envelope parameters, applied to a voice when its note starts.
    public double AttackSeconds { get; set; } = 0.005;
    public double DecaySeconds { get; set; } = 0.08;
    public double SustainLevel { get; set; } = 0.7;
    public double ReleaseSeconds { get; set; } = 0.2;

    public override string Name => "Oscillator";

    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Waveform", new[] { "Sine", "Sawtooth", "Square" },
            () => (int)Waveform, i => Waveform = (Waveform)i) { Group = "Oscillator" },
        new FloatParameter("Attack", 0.001, 2.0, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Decay", 0.001, 2.0, () => DecaySeconds, v => DecaySeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Sustain", 0.0, 1.0, () => SustainLevel, v => SustainLevel = v) { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 3.0, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s") { Group = "Amp Envelope" }
    };

    public override IInstrument Clone() => new OscillatorInstrument
    {
        Waveform = Waveform,
        AttackSeconds = AttackSeconds,
        DecaySeconds = DecaySeconds,
        SustainLevel = SustainLevel,
        ReleaseSeconds = ReleaseSeconds
    };

    protected override Voice CreateVoice() => new OscillatorVoice(this);

    /// <summary>One oscillator + envelope. References its instrument to read live parameters.</summary>
    private sealed class OscillatorVoice : Voice
    {
        // Per-voice output gain so a stack of voices stays clear of clipping.
        private const float VoiceGain = 0.22f;

        private readonly OscillatorInstrument _instrument;
        private readonly Oscillator _oscillator = new();
        private readonly AdsrEnvelope _envelope = new();
        private float _velocity;

        public OscillatorVoice(OscillatorInstrument instrument) => _instrument = instrument;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;

            _oscillator.SetSampleRate(format.SampleRate);
            _oscillator.SetFrequency(MusicalMath.NoteToFrequency(midiNote));
            _oscillator.ResetPhase();

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
                // Pick up live waveform changes.
                _oscillator.Waveform = _instrument.Waveform;

                var sample = _oscillator.Next() * _envelope.Process() * _velocity * VoiceGain;

                var baseIndex = frame * channels;
                for (var ch = 0; ch < channels; ch++)
                {
                    buffer[baseIndex + ch] += sample;
                }

                if (!_envelope.IsActive)
                {
                    IsActive = false;
                    return;
                }
            }
        }
    }
}
