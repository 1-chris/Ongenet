using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
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

    protected override string GetTypeId() => TypeId;
    private const int RootNote = 60; // C4 plays the sample at its original pitch

    private volatile AudioSampleBuffer? _sample;

    public int VoiceMode { get; set; }
    public double AttackSeconds { get; set; } = 0.001;
    public double ReleaseSeconds { get; set; } = 0.08;
    public double Gain { get; set; } = 0.9;
    public double Damping { get; set; } = 0.5;
    public double PickPosition { get; set; } = 0.5;
    public double Brightness { get; set; } = 0.5;
    /// <summary>Fine pitch offset in cents (FL channel pitch shift), applied on top of MIDI note.</summary>
    public double FinePitchCents { get; set; }

    private static readonly string[] VoiceModeNames = { "Sample", "Karplus" };

    /// <summary>Absolute path of a sample pending decode (import deferral), or null.</summary>
    public string? SampleFilePath { get; set; }

    public string? SampleName { get; private set; }

    public override string Name => "Basic Sampler";

    public AudioSampleBuffer? Sample => _sample;
    public AudioSampleBuffer? CurrentSample => _sample;

    public void LoadSample(AudioSampleBuffer sample, string name)
    {
        _sample = sample;
        SampleName = name;
    }

    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Voice", VoiceModeNames, () => VoiceMode, v => VoiceMode = v) { Group = "Voice" },
        new FloatParameter("Damping", 0, 1, () => Damping, v => Damping = v, "0.00") { Group = "Voice" },
        new FloatParameter("Pick", 0, 1, () => PickPosition, v => PickPosition = v, "0.00") { Group = "Voice" },
        new FloatParameter("Bright", 0, 1, () => Brightness, v => Brightness = v, "0.00") { Group = "Voice" },
        new FloatParameter("Attack", 0.0, 1.0, () => AttackSeconds, v => AttackSeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Release", 0.001, 2.0, () => ReleaseSeconds, v => ReleaseSeconds = v, "0.000", "s") { Group = "Amp Envelope" },
        new FloatParameter("Fine Pitch", -2400, 2400, () => FinePitchCents, v => FinePitchCents = v, "0", "ct") { Group = "Pitch" },
        new FloatParameter("Gain", 0.0, 1.0, () => Gain, v => Gain = v) { Group = "Output" }
    };

    public override IInstrument Clone()
    {
        var copy = new BasicSamplerInstrument
        {
            VoiceMode = VoiceMode,
            AttackSeconds = AttackSeconds,
            ReleaseSeconds = ReleaseSeconds,
            Gain = Gain,
            Damping = Damping,
            PickPosition = PickPosition,
            Brightness = Brightness,
            FinePitchCents = FinePitchCents,
            SampleFilePath = SampleFilePath
        };
        if (_sample is { } s && SampleName is { } n) copy.LoadSample(s, n);
        return copy;
    }

    protected override Voice CreateVoice() => new SampleVoice(this);

    private sealed class SampleVoice : Voice
    {
        private readonly BasicSamplerInstrument _instrument;
        private readonly AdsrEnvelope _envelope = new();
        private readonly KarplusStrongDsp _karplus = new();
        private AudioSampleBuffer? _sample;
        private double _position;       // read position, in file frames
        private double _rate;           // file frames advanced per output frame
        private float _velocity;
        private bool _useKarplus;

        public SampleVoice(BasicSamplerInstrument instrument) => _instrument = instrument;

        public override void Start(int midiNote, float velocity, AudioFormat format)
        {
            base.Start(midiNote, velocity, format);
            _velocity = velocity;
            _sample = _instrument.Sample;
            _position = 0;
            _useKarplus = _instrument.VoiceMode == 1;

            if (_useKarplus)
            {
                _karplus.Prepare(format.SampleRate);
                _karplus.Damping = _instrument.Damping;
                _karplus.PickPosition = _instrument.PickPosition;
                _karplus.Brightness = _instrument.Brightness;
                _karplus.SetFrequency(MusicalMath.NoteToFrequency(midiNote));
                _karplus.Pluck(velocity);
            }
            else if (_sample is not null)
            {
                var pitch = Math.Pow(2.0, (midiNote - RootNote) / 12.0 + _instrument.FinePitchCents / 1200.0);
                _rate = (double)_sample.SampleRate / format.SampleRate * pitch;
            }

            _envelope.SetSampleRate(format.SampleRate);
            _envelope.AttackSeconds = _instrument.AttackSeconds;
            _envelope.DecaySeconds = 0.0;
            _envelope.SustainLevel = 1.0;
            _envelope.ReleaseSeconds = _instrument.ReleaseSeconds;
            _envelope.Gate();

            if (!_useKarplus && _sample is null) IsActive = false;
        }

        public override void Release() => _envelope.Release();

        public override void Render(Span<float> buffer)
        {
            if (_useKarplus)
            {
                RenderKarplus(buffer);
                return;
            }

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

        private void RenderKarplus(Span<float> buffer)
        {
            var channels = Format.Channels < 1 ? 1 : Format.Channels;
            var frames = buffer.Length / channels;
            var gain = (float)_instrument.Gain;

            for (var frame = 0; frame < frames; frame++)
            {
                var env = _envelope.Process();
                var s = _karplus.Process() * env * _velocity * gain;
                var baseIndex = frame * channels;
                for (var c = 0; c < channels; c++) buffer[baseIndex + c] += s;

                if (!_envelope.IsActive && _karplus.IsSilent())
                {
                    IsActive = false;
                    return;
                }
            }
        }
    }
}
