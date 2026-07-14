using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>Compressor+ with multiband analysis and character modes.</summary>
public sealed class CompressorPlusEffect : IAudioEffect, IGainReductionSource
{
    public const string TypeId = "compressor_plus";

    private static readonly string[] CharacterNames = { "Clean", "Punch", "Glue", "VCA" };
    private const double LowCrossHz = 180.0;
    private const double HighCrossHz = 2800.0;

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Compressor+";
    public bool Enabled { get; set; } = true;

    public double ThresholdDb { get; set; } = -18.0;
    public double Ratio { get; set; } = 4.0;
    public double AttackMs { get; set; } = 10.0;
    public double ReleaseMs { get; set; } = 120.0;
    public double MakeupDb { get; set; }
    public int Character { get; set; }
    public bool Multiband { get; set; } = true;

    public double GainReductionDb { get; private set; }

    private int _channels = 2;
    private double _sampleRate = 44100;
    private readonly EnvelopeFollower _follower = new();
    private EnvelopeFollower[] _bandFollowers = Array.Empty<EnvelopeFollower>();
    private Biquad[] _lp = Array.Empty<Biquad>();
    private Biquad[] _hp = Array.Empty<Biquad>();
    private BiquadCoefficients _lpC = BiquadCoefficients.Identity;
    private BiquadCoefficients _hpC = BiquadCoefficients.Identity;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Threshold", -60, 0, () => ThresholdDb, v => ThresholdDb = v, "0.#", "dB"),
        new FloatParameter("Ratio", 1, 20, () => Ratio, v => Ratio = v, "0.#", ":1"),
        new FloatParameter("Attack", 0.1, 200, () => AttackMs, v => AttackMs = v, "0.#", "ms", 2.0),
        new FloatParameter("Release", 5, 1000, () => ReleaseMs, v => ReleaseMs = v, "0", "ms", 2.0),
        new FloatParameter("Makeup", 0, 24, () => MakeupDb, v => MakeupDb = v, "0.#", "dB"),
        new ChoiceParameter("Character", CharacterNames, () => Character, i => Character = i),
        new BoolParameter("Multiband", () => Multiband, v => Multiband = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _lpC = BiquadCoefficients.Compute(FilterMode.LowPass, LowCrossHz, 0.707, _sampleRate);
        _hpC = BiquadCoefficients.Compute(FilterMode.HighPass, HighCrossHz, 0.707, _sampleRate);
        _lp = new Biquad[_channels];
        _hp = new Biquad[_channels];
        _bandFollowers = new EnvelopeFollower[_channels * 3];
        for (var i = 0; i < _bandFollowers.Length; i++)
        {
            _bandFollowers[i] = new EnvelopeFollower();
            _bandFollowers[i].SetTimes(AttackMs, ReleaseMs, _sampleRate);
        }
        _follower.Reset();
    }

    public IAudioEffect Clone() => new CompressorPlusEffect
    {
        Enabled = Enabled, ThresholdDb = ThresholdDb, Ratio = Ratio,
        AttackMs = AttackMs, ReleaseMs = ReleaseMs, MakeupDb = MakeupDb,
        Character = Character, Multiband = Multiband
    };

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        var ch = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / ch;
        var ratio = RatioForCharacter();
        var makeup = (float)AudioMath.Db2Lin(MakeupDb);
        var grPeak = 0.0;

        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < ch; c++)
            {
                var i = f * ch + c;
                var x = buffer[i];
                var gain = Multiband
                    ? ProcessMultiband(c, x, ratio, ref grPeak)
                    : ProcessSingle(x, ratio, ref grPeak);
                buffer[i] = x * gain * makeup;
            }
        }

        GainReductionDb = grPeak;
    }

    private float ProcessSingle(float x, double ratio, ref double grPeak)
    {
        _follower.SetTimes(AttackMs, ReleaseMs, _sampleRate);
        var env = _follower.Process(Math.Abs(x));
        var db = AudioMath.Lin2Db(env + 1e-9);
        var gr = db <= ThresholdDb ? 0.0 : (ThresholdDb - db) * (1.0 - 1.0 / ratio);
        if (gr < grPeak) grPeak = gr;
        return (float)AudioMath.Db2Lin(gr);
    }

    private float ProcessMultiband(int ch, float x, double ratio, ref double grPeak)
    {
        var low = (float)_lp[ch].Process(_lpC, x);
        var high = (float)_hp[ch].Process(_hpC, x);
        var mid = x - low - high;
        var g = 0f;
        for (var b = 0; b < 3; b++)
        {
            var band = b switch { 0 => low, 1 => mid, _ => high };
            var idx = ch * 3 + b;
            _bandFollowers[idx].SetTimes(AttackMs, ReleaseMs, _sampleRate);
            var env = _bandFollowers[idx].Process(Math.Abs(band));
            var db = AudioMath.Lin2Db(env + 1e-9);
            var gr = db <= ThresholdDb ? 0.0 : (ThresholdDb - db) * (1.0 - 1.0 / ratio);
            if (gr < grPeak) grPeak = gr;
            g += band * (float)AudioMath.Db2Lin(gr);
        }
        return g;
    }

    private double RatioForCharacter() => Character switch
    {
        1 => Ratio * 1.25,
        2 => Ratio * 0.85,
        3 => Ratio * 1.5,
        _ => Ratio
    };
}
