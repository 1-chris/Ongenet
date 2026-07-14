using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// An LFO-swept band-pass filter: Rate and Depth modulate the centre frequency around Center,
/// mixed with the dry signal.
/// </summary>
public sealed class SweepEffect : IAudioEffect
{
    public const string TypeId = "sweep";

    string IAudioEffect.TypeId => TypeId;

    private const double MinHz = 80.0;
    private const double MaxHz = 12000.0;

    public bool Enabled { get; set; } = true;

    public double RateHz { get; set; } = 0.5;
    public double Depth { get; set; } = 0.6;
    public double Center { get; set; } = 1000.0;
    public double Mix { get; set; } = 0.5;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _bq = Array.Empty<Biquad>();
    private BiquadCoefficients _coeffs = BiquadCoefficients.Identity;
    private readonly Lfo _lfo = new();
    private double _lastFc = double.NaN;

    public string Name => "Sweep";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Rate", 0.05, 8.0, () => RateHz, v => RateHz = v, "0.##", "Hz", 2.0),
        new FloatParameter("Depth", 0.0, 1.0, () => Depth, v => Depth = v),
        new FloatParameter("Center", 80.0, 12000.0, () => Center, v => Center = v, "0", "Hz", 2.0),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _bq = new Biquad[_channels];
        _lfo.Reset();
        _lastFc = double.NaN;
    }

    public IAudioEffect Clone() => new SweepEffect
    {
        Enabled = Enabled, RateHz = RateHz, Depth = Depth, Center = Center, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var bq = _bq;
        if (bq.Length == 0) return;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, bq.Length);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var depth = Math.Clamp(Depth, 0, 1);
        var center = Math.Clamp(Center, MinHz, MaxHz);
        _lfo.SetRate(RateHz, _sampleRate);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var lfo = _lfo.Value(0);
            var fc = center * Math.Pow(2.0, depth * lfo * 2.0);
            fc = Math.Clamp(fc, MinHz, MaxHz);

            if (Math.Abs(fc - _lastFc) > 1.0)
            {
                _coeffs = BiquadCoefficients.Compute(FilterMode.BandPass, fc, 1.5, _sampleRate);
                _lastFc = fc;
            }

            var coeffs = _coeffs;
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var wet = (float)bq[c].Process(coeffs, dry);
                buffer[i + c] = dry * (1 - mix) + wet * mix;
            }

            _lfo.Advance();
        }
    }
}
