using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Mid-side focus: a narrow band-pass boost at the centre frequency emphasises the mono image
/// while Amount controls how strongly the focused band is blended in.
/// </summary>
public sealed class FocusEffect : IAudioEffect
{
    public const string TypeId = "focus";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Amount { get; set; } = 0.5;
    public double Frequency { get; set; } = 1000.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _bp = Array.Empty<Biquad>();
    private BiquadCoefficients _coeffs = BiquadCoefficients.Identity;
    private double _lastFreq = double.NaN, _lastSr = double.NaN;

    public string Name => "Focus";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Amount", 0.0, 1.0, () => Amount, v => Amount = v),
        new FloatParameter("Frequency", 80.0, 12000.0, () => Frequency, v => Frequency = v, "0", "Hz", 2.0)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _bp = new Biquad[_channels];
        _lastFreq = double.NaN;
    }

    public IAudioEffect Clone() => new FocusEffect
    {
        Enabled = Enabled, Amount = Amount, Frequency = Frequency
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        if (Frequency != _lastFreq || _sampleRate != _lastSr)
        {
            _coeffs = BiquadCoefficients.Compute(FilterMode.BandPass, Frequency, 2.5, _sampleRate);
            _lastFreq = Frequency;
            _lastSr = _sampleRate;
        }

        var amount = (float)Math.Clamp(Amount, 0, 1);
        var coeffs = _coeffs;
        var frames = buffer.Length / channels;

        if (channels < 2)
        {
            var bq = _bp[0];
            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                var dry = buffer[i];
                var focused = (float)bq.Process(coeffs, dry);
                buffer[i] = dry + (focused - dry) * amount;
            }

            return;
        }

        var bp = _bp;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var l = buffer[i];
            var r = buffer[i + 1];
            var mid = (l + r) * 0.5f;
            var side = (l - r) * 0.5f;
            var focused = (float)bp[0].Process(coeffs, mid);
            mid = mid + (focused - mid) * amount;
            buffer[i] = mid + side;
            buffer[i + 1] = mid - side;
        }
    }
}
