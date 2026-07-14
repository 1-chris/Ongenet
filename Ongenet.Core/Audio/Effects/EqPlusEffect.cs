using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Multi-band EQ with a dynamic mid band: EnvelopeFollower on the mid drives additional gain.
/// </summary>
public sealed class EqPlusEffect : IAudioEffect
{
    public const string TypeId = "eq_plus";

    string IAudioEffect.TypeId => TypeId;

    private const double LowCrossHz = 250.0;
    private const double HighCrossHz = 2500.0;

    public bool Enabled { get; set; } = true;

    public double LowGainDb { get; set; }
    public double MidGainDb { get; set; }
    public double HighGainDb { get; set; }
    public double DynamicsDb { get; set; } = 6.0;
    public double ThresholdDb { get; set; } = -24.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _lp = Array.Empty<Biquad>();
    private Biquad[] _hp = Array.Empty<Biquad>();
    private BiquadCoefficients _lpC = BiquadCoefficients.Identity;
    private BiquadCoefficients _hpC = BiquadCoefficients.Identity;
    private EnvelopeFollower[] _env = Array.Empty<EnvelopeFollower>();

    public string Name => "EQ+";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Low", -18.0, 18.0, () => LowGainDb, v => LowGainDb = v, "0.#", "dB"),
        new FloatParameter("Mid", -18.0, 18.0, () => MidGainDb, v => MidGainDb = v, "0.#", "dB"),
        new FloatParameter("High", -18.0, 18.0, () => HighGainDb, v => HighGainDb = v, "0.#", "dB"),
        new FloatParameter("Dynamics", 0.0, 18.0, () => DynamicsDb, v => DynamicsDb = v, "0.#", "dB"),
        new FloatParameter("Threshold", -60.0, 0.0, () => ThresholdDb, v => ThresholdDb = v, "0.#", "dB")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _lp = new Biquad[_channels];
        _hp = new Biquad[_channels];
        var env = new EnvelopeFollower[_channels];
        for (var c = 0; c < _channels; c++)
        {
            env[c] = new EnvelopeFollower();
            env[c].SetTimes(5.0, 80.0, _sampleRate);
        }

        _env = env;
        _lpC = BiquadCoefficients.Compute(FilterMode.LowPass, LowCrossHz, 0.707, _sampleRate);
        _hpC = BiquadCoefficients.Compute(FilterMode.HighPass, HighCrossHz, 0.707, _sampleRate);
    }

    public IAudioEffect Clone() => new EqPlusEffect
    {
        Enabled = Enabled, LowGainDb = LowGainDb, MidGainDb = MidGainDb,
        HighGainDb = HighGainDb, DynamicsDb = DynamicsDb, ThresholdDb = ThresholdDb
    };

    public void Process(Span<float> buffer)
    {
        var channels = Math.Min(_channels < 1 ? 1 : _channels, _lp.Length);
        if (channels <= 0 || _hp.Length < channels || _env.Length < channels) return;

        var lpC = _lpC;
        var hpC = _hpC;
        var lowG = (float)AudioMath.Db2Lin(LowGainDb);
        var midBase = MidGainDb;
        var highG = (float)AudioMath.Db2Lin(HighGainDb);
        var dynRange = DynamicsDb;
        var threshold = ThresholdDb;
        var lp = _lp;
        var hp = _hp;
        var env = _env;

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

                var rect = mid < 0 ? -mid : mid;
                var level = env[c].Process(rect);
                var over = AudioMath.Lin2Db(level) - threshold;
                var dynDb = over > 0 ? Math.Min(dynRange, over * (dynRange / 12.0)) : 0;
                var midG = (float)AudioMath.Db2Lin(midBase + dynDb);

                buffer[i + c] = low * lowG + mid * midG + high * highG;
            }
        }
    }
}
