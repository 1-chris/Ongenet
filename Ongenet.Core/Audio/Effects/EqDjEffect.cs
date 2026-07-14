using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// DJ-style three-band isolator: Low / Mid / High shelves with crossover filters and per-band gain.
/// </summary>
public sealed class EqDjEffect : IAudioEffect
{
    public const string TypeId = "eq_dj";

    string IAudioEffect.TypeId => TypeId;

    private const double LowCrossHz = 250.0;
    private const double HighCrossHz = 2500.0;

    public bool Enabled { get; set; } = true;

    public double LowGainDb { get; set; }
    public double MidGainDb { get; set; }
    public double HighGainDb { get; set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _lp = Array.Empty<Biquad>();
    private Biquad[] _hp = Array.Empty<Biquad>();
    private BiquadCoefficients _lpC = BiquadCoefficients.Identity;
    private BiquadCoefficients _hpC = BiquadCoefficients.Identity;

    public string Name => "EQ DJ";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        // Deep floor approximates isolator kill (−∞) while remaining UI-friendly.
        new FloatParameter("Low", -70.0, 6.0, () => LowGainDb, v => LowGainDb = v, "0.#", "dB"),
        new FloatParameter("Mid", -70.0, 6.0, () => MidGainDb, v => MidGainDb = v, "0.#", "dB"),
        new FloatParameter("High", -70.0, 6.0, () => HighGainDb, v => HighGainDb = v, "0.#", "dB")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _lp = new Biquad[_channels];
        _hp = new Biquad[_channels];
        _lpC = BiquadCoefficients.Compute(FilterMode.LowPass, LowCrossHz, 0.707, _sampleRate);
        _hpC = BiquadCoefficients.Compute(FilterMode.HighPass, HighCrossHz, 0.707, _sampleRate);
    }

    public IAudioEffect Clone() => new EqDjEffect
    {
        Enabled = Enabled, LowGainDb = LowGainDb, MidGainDb = MidGainDb, HighGainDb = HighGainDb
    };

    public void Process(Span<float> buffer)
    {
        var channels = Math.Min(_channels < 1 ? 1 : _channels, _lp.Length);
        if (channels <= 0 || _hp.Length < channels) return;

        var lpC = _lpC;
        var hpC = _hpC;
        var lowG = (float)AudioMath.Db2Lin(LowGainDb);
        var midG = (float)AudioMath.Db2Lin(MidGainDb);
        var highG = (float)AudioMath.Db2Lin(HighGainDb);
        var lp = _lp;
        var hp = _hp;

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var low = (float)lp[c].Process(lpC, dry);
                var high = (float)hp[c].Process(hpC, dry);
                var mid = dry - low - high;
                buffer[i + c] = low * lowG + mid * midG + high * highG;
            }
        }
    }
}
