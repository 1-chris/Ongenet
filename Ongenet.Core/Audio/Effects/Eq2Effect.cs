using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Two-band peaking EQ: Low and High bell filters.
/// </summary>
public sealed class Eq2Effect : IAudioEffect
{
    public const string TypeId = "eq2";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double LowHz { get; set; } = 200.0;
    public double LowGainDb { get; set; }
    public double HighHz { get; set; } = 4000.0;
    public double HighGainDb { get; set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _low = Array.Empty<Biquad>();
    private Biquad[] _high = Array.Empty<Biquad>();
    private BiquadCoefficients _lowC = BiquadCoefficients.Identity;
    private BiquadCoefficients _highC = BiquadCoefficients.Identity;
    private double _lastLowHz = double.NaN, _lastLowG = double.NaN;
    private double _lastHighHz = double.NaN, _lastHighG = double.NaN, _lastSr = double.NaN;

    public string Name => "EQ 2";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Low Freq", 40.0, 1000.0, () => LowHz, v => LowHz = v, "0", "Hz", 2.0),
        new FloatParameter("Low Gain", -18.0, 18.0, () => LowGainDb, v => LowGainDb = v, "0.#", "dB"),
        new FloatParameter("High Freq", 1000.0, 16000.0, () => HighHz, v => HighHz = v, "0", "Hz", 2.0),
        new FloatParameter("High Gain", -18.0, 18.0, () => HighGainDb, v => HighGainDb = v, "0.#", "dB")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _low = new Biquad[_channels];
        _high = new Biquad[_channels];
        _lastLowHz = double.NaN;
    }

    public IAudioEffect Clone() => new Eq2Effect
    {
        Enabled = Enabled, LowHz = LowHz, LowGainDb = LowGainDb,
        HighHz = HighHz, HighGainDb = HighGainDb
    };

    public void Process(Span<float> buffer)
    {
        var channels = Math.Min(_channels < 1 ? 1 : _channels, _low.Length);
        if (channels <= 0 || _high.Length < channels) return;

        if (LowHz != _lastLowHz || LowGainDb != _lastLowG || HighHz != _lastHighHz ||
            HighGainDb != _lastHighG || _sampleRate != _lastSr)
        {
            _lowC = BiquadCoefficients.ComputeEq(EqBandType.Bell, LowHz, 1.0, LowGainDb, _sampleRate);
            _highC = BiquadCoefficients.ComputeEq(EqBandType.Bell, HighHz, 1.0, HighGainDb, _sampleRate);
            _lastLowHz = LowHz; _lastLowG = LowGainDb;
            _lastHighHz = HighHz; _lastHighG = HighGainDb; _lastSr = _sampleRate;
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
