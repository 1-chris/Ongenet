using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A harmonic enhancer: high-pass the input, waveshape the bright band, optional bass harmonic
/// enhancement, then blend back with the dry signal.
/// </summary>
public sealed class ExciterEffect : IAudioEffect
{
    public const string TypeId = "exciter";

    string IAudioEffect.TypeId => TypeId;

    private static readonly string[] ModeNames = { "Tanh", "Hard Clip", "Foldback", "Sine Fold" };

    public bool Enabled { get; set; } = true;

    public double Drive { get; set; } = 4.0;
    public double Mix { get; set; } = 0.35;
    public double ToneHz { get; set; } = 3500.0;
    public int Mode { get; set; }
    public double OutputDb { get; set; }
    public double BassEnhance { get; set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _hp = new Biquad[2];
    private BassHarmonicEnhancerDsp[] _bass = Array.Empty<BassHarmonicEnhancerDsp>();
    private BiquadCoefficients _coeffs = BiquadCoefficients.Identity;
    private double _lastTone = double.NaN, _lastSr = double.NaN;

    public string Name => "Exciter";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Drive", 1.0, 24.0, () => Drive, v => Drive = v, "0.0"),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new FloatParameter("Tone", 200.0, 12000.0, () => ToneHz, v => ToneHz = v, "0", "Hz", 3.0),
        new ChoiceParameter("Mode", ModeNames, () => Mode, v => Mode = v),
        new FloatParameter("Output", -24.0, 12.0, () => OutputDb, v => OutputDb = v, "0.#", "dB"),
        new FloatParameter("Bass Enhance", 0.0, 1.0, () => BassEnhance, v => BassEnhance = v)
    };

    public void Prepare(AudioFormat format)
    {
        var sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var hp = new Biquad[channels];
        var bass = new BassHarmonicEnhancerDsp[channels];
        for (var c = 0; c < channels; c++)
        {
            bass[c] = new BassHarmonicEnhancerDsp();
            bass[c].Prepare(sampleRate);
        }

        // Publish fully-built arrays with single assignments — RebuildTracks can call Prepare from the UI
        // thread while Process runs on the audio worker pool (e.g. after "Render clip to new track").
        _sampleRate = sampleRate;
        _channels = channels;
        _hp = hp;
        _bass = bass;
        _lastTone = double.NaN;
    }

    public IAudioEffect Clone() => new ExciterEffect
    {
        Enabled = Enabled,
        Drive = Drive,
        Mix = Mix,
        ToneHz = ToneHz,
        Mode = Mode,
        OutputDb = OutputDb,
        BassEnhance = BassEnhance
    };

    public void Process(Span<float> buffer)
    {
        var channels = Math.Min(_channels < 1 ? 1 : _channels, _hp.Length);
        if (channels <= 0) return;

        if (ToneHz != _lastTone || _sampleRate != _lastSr)
        {
            _coeffs = BiquadCoefficients.Compute(FilterMode.HighPass, ToneHz, 0.707, _sampleRate);
            _lastTone = ToneHz;
            _lastSr = _sampleRate;
        }

        var coeffs = _coeffs;
        var drive = (float)Math.Max(1e-6, Drive);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var output = (float)AudioMath.Db2Lin(OutputDb);
        var bassAmt = (float)Math.Clamp(BassEnhance, 0, 1);
        var type = (ShaperType)Math.Clamp(Mode, 0, 3);
        var hp = _hp;
        var bass = _bass;
        if (hp.Length < channels || bass.Length < channels) return;

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var bassDsp = bass[c];
                if (bassDsp is null) return;
                bassDsp.Amount = bassAmt;
                var withBass = bassDsp.Process(dry);
                var bright = (float)hp[c].Process(coeffs, withBass);
                var excited = WaveShaper.Shape(bright, type, drive);
                buffer[i + c] = (dry * (1 - mix) + excited * mix) * output;
            }
        }
    }
}
