using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Soft overdrive distinct from hard distortion: gentle tanh shaping with asymmetry bias and a
/// tone (low-pass) control, plus dry/wet mix.
/// </summary>
public sealed class OverEffect : IAudioEffect
{
    public const string TypeId = "over";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Drive { get; set; } = 3.0;
    public double Tone { get; set; } = 0.6;
    public double Mix { get; set; } = 1.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _tone = Array.Empty<Biquad>();
    private BiquadCoefficients _coeffs = BiquadCoefficients.Identity;
    private double _lastTone = double.NaN, _lastSr = double.NaN;

    public string Name => "Over";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Drive", 1.0, 16.0, () => Drive, v => Drive = v, "0.0"),
        new FloatParameter("Tone", 0.0, 1.0, () => Tone, v => Tone = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _tone = new Biquad[_channels];
        _lastTone = double.NaN;
    }

    public IAudioEffect Clone() => new OverEffect
    {
        Enabled = Enabled, Drive = Drive, Tone = Tone, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var channels = Math.Min(_channels < 1 ? 1 : _channels, _tone.Length);
        if (channels <= 0) return;

        var tone = Math.Clamp(Tone, 0, 1);
        if (tone != _lastTone || _sampleRate != _lastSr)
        {
            var freq = 600.0 * Math.Pow(25.0, tone);
            _coeffs = BiquadCoefficients.Compute(FilterMode.LowPass, freq, 0.707, _sampleRate);
            _lastTone = tone;
            _lastSr = _sampleRate;
        }

        var drive = (float)Math.Max(1e-6, Drive);
        var bias = 0.08f;
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var coeffs = _coeffs;
        var bq = _tone;
        var frames = buffer.Length / channels;

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var shaped = WaveShaper.Shape(dry, ShaperType.Tanh, drive, bias);
                var toned = (float)bq[c].Process(coeffs, shaped);
                buffer[i + c] = (dry * (1 - mix) + toned * mix);
            }
        }
    }
}
