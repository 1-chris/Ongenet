using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Tilt EQ: complementary low/high shelves around PivotHz. Positive AmountDb brightens;
/// negative darkens.
/// </summary>
public sealed class TiltEffect : IAudioEffect
{
    public const string TypeId = "tilt";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double AmountDb { get; set; }
    public double PivotHz { get; set; } = 1000.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _low = Array.Empty<Biquad>();
    private Biquad[] _high = Array.Empty<Biquad>();
    private BiquadCoefficients _lowC = BiquadCoefficients.Identity;
    private BiquadCoefficients _highC = BiquadCoefficients.Identity;
    private double _lastAmount = double.NaN, _lastPivot = double.NaN, _lastSr = double.NaN;

    public string Name => "Tilt";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Amount", -12.0, 12.0, () => AmountDb, v => AmountDb = v, "0.#", "dB"),
        new FloatParameter("Pivot", 100.0, 10000.0, () => PivotHz, v => PivotHz = v, "0", "Hz", 2.0)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _low = new Biquad[_channels];
        _high = new Biquad[_channels];
        _lastAmount = double.NaN;
    }

    public IAudioEffect Clone() => new TiltEffect
    {
        Enabled = Enabled, AmountDb = AmountDb, PivotHz = PivotHz
    };

    public void Process(Span<float> buffer)
    {
        var channels = Math.Min(_channels < 1 ? 1 : _channels, _low.Length);
        if (channels <= 0 || _high.Length < channels) return;

        if (AmountDb != _lastAmount || PivotHz != _lastPivot || _sampleRate != _lastSr)
        {
            var amount = Math.Clamp(AmountDb, -12.0, 12.0);
            var pivot = PivotHz;
            _lowC = BiquadCoefficients.ComputeEq(EqBandType.LowShelf, pivot, 0.7, -amount, _sampleRate);
            _highC = BiquadCoefficients.ComputeEq(EqBandType.HighShelf, pivot, 0.7, amount, _sampleRate);
            _lastAmount = AmountDb;
            _lastPivot = PivotHz;
            _lastSr = _sampleRate;
        }

        var lowC = _lowC;
        var highC = _highC;
        var low = _low;
        var high = _high;

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var s = (float)low[c].Process(lowC, buffer[i + c]);
                buffer[i + c] = (float)high[c].Process(highC, s);
            }
        }
    }
}
