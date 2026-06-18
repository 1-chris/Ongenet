using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Plays a single user-loaded audio sample as a pitched instrument: each note resamples the sample
/// by 2^((note − root)/12) (root = C4), shaped by an attack/release envelope. "Basic" = one sample.
/// </summary>
public sealed class BasicSamplerInstrument : PolyphonicInstrument, ISampleHost
{
    public const string TypeId = "sampler";
    private const int RootNote = 60; // C4 plays the sample at its original pitch

    private volatile AudioSampleBuffer? _sample;

    public double AttackSeconds { get; set; } = 0.001;
    public double ReleaseSeconds { get; set; } = 0.08;
    public double Gain { get; set; } = 0.9;

    public string? SampleName { get; private set; }

    public override string Name => "Basic Sampler";

    public AudioSampleBuffer? Sample => _sample;

    public void LoadSample(AudioSampleBuffer sample, string name)
    {
        _sample = sample;
        SampleName = name;
    }

    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Attack", 0.0, 1.0, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 2.0, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Gain", 0.0, 1.0, () => Gain, v => Gain = v) { Group = "Output" }
    };

    public override IInstrument Clone()
    {
        var copy = new BasicSamplerInstrument
        {
            AttackSeconds = AttackSeconds,
            ReleaseSeconds = ReleaseSeconds,
            Gain = Gain
        };
        if (_sample is { } s && SampleName is { } n) copy.LoadSample(s, n);
        return copy;
    }

    protected override Voice CreateVoice() => new SampleVoice(this);

    private sealed class SampleVoice : Voice
    {
        private readonly BasicSamplerInstrument _instrument;
        private readonly AdsrEnvelope _envelope = new();
        private AudioSampleBuffer? _sample;
        private double _position;       // read position, in file frames
        private double _rate;           // file frames advanced per output frame
        private float _velocity;

        public SampleVoice(BasicSamplerInstrument instrument) => _instrument = instrument;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            _sample = _instrument.Sample;
            _position = 0;

            if (_sample is not null)
            {
                var pitch = Math.Pow(2.0, (midiNote - RootNote) / 12.0);
                _rate = (double)_sample.SampleRate / format.SampleRate * pitch;
            }

            _envelope.SetSampleRate(format.SampleRate);
            _envelope.AttackSeconds = _instrument.AttackSeconds;
            _envelope.DecaySeconds = 0.0;
            _envelope.SustainLevel = 1.0;
            _envelope.ReleaseSeconds = _instrument.ReleaseSeconds;
            _envelope.Gate();

            if (_sample is null) IsActive = false; // nothing loaded
        }

        public override void Release() => _envelope.Release();

        public override void Render(Span<float> buffer)
        {
            var sample = _sample;
            if (sample is null) { IsActive = false; return; }

            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var frameCount = sample.FrameCount;
            var gain = (float)_instrument.Gain;

            for (var frame = 0; frame < frames; frame++)
            {
                var f0 = (long)_position;
                if (f0 >= frameCount) { IsActive = false; return; }

                var frac = (float)(_position - f0);
                var env = _envelope.Process();
                var baseIndex = frame * channels;

                for (var c = 0; c < channels; c++)
                {
                    var fileChannel = c < sample.Channels ? c : sample.Channels - 1;
                    var s0 = sample.Sample(f0, fileChannel);
                    var s1 = sample.Sample(f0 + 1, fileChannel);
                    var s = s0 + (s1 - s0) * frac;
                    buffer[baseIndex + c] += s * env * _velocity * gain;
                }

                _position += _rate;

                if (!_envelope.IsActive) { IsActive = false; return; }
            }
        }
    }
}
