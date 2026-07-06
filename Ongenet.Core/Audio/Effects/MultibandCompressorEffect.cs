using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// An "OTT"-style three-band up/down compressor — the standard modern-trance wall-of-sound tool.
/// The signal is split into low / mid / high bands; each band is both compressed downward (peaks
/// clamped) and upward (quiet detail and harmonics pushed up), which inflates the sound so the lead
/// seems to explode in front of the listener. A high-band boost adds the crystalline top, and a
/// Depth control blends the whole effect back toward dry.
/// </summary>
public sealed class MultibandCompressorEffect : IAudioEffect
{
    public const string TypeId = "multiband_comp";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    /// <summary>Dry/wet blend of the processed multiband signal (0 = bypass, 1 = full OTT).</summary>
    public double Depth { get; set; } = 0.4;

    /// <summary>Extra top-end lift on the high band (dB) — the "inflated" trance sheen.</summary>
    public double HighBoostDb { get; set; } = 3.0;

    // Fixed crossover points (Hz): low/mid around the body split, mid/high around the presence split.
    private const double LowCrossHz = 200.0;
    private const double HighCrossHz = 2500.0;

    // OTT band behaviour: a single threshold per band — clamp downward above it, lift upward below
    // it — so loud peaks are tamed while quiet detail and harmonics are pushed up ("inflated").
    private const double ThresholdDb = -30.0;
    private const double DownRatio = 4.0;
    private const double UpRatio = 3.0;
    private const double MaxUpwardDb = 12.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;

    private BiquadCoefficients _lp = BiquadCoefficients.Identity;
    private BiquadCoefficients _hp = BiquadCoefficients.Identity;

    // Per-channel band-split filters and per-channel/per-band envelope followers.
    private Biquad[] _lpState = Array.Empty<Biquad>();
    private Biquad[] _hpState = Array.Empty<Biquad>();
    private EnvelopeFollower[,] _env = new EnvelopeFollower[0, 0];

    public string Name => "Multiband (OTT)";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Depth", 0.0, 1.0, () => Depth, v => Depth = v),
        new FloatParameter("High Boost", 0.0, 9.0, () => HighBoostDb, v => HighBoostDb = v, "0.#", "dB")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;

        _lp = BiquadCoefficients.Compute(FilterMode.LowPass, LowCrossHz, 0.707, _sampleRate);
        _hp = BiquadCoefficients.Compute(FilterMode.HighPass, HighCrossHz, 0.707, _sampleRate);

        _lpState = new Biquad[_channels];
        _hpState = new Biquad[_channels];
        _env = new EnvelopeFollower[_channels, 3];
        for (var c = 0; c < _channels; c++)
        {
            _lpState[c].Reset();
            _hpState[c].Reset();
            for (var b = 0; b < 3; b++)
            {
                _env[c, b] = new EnvelopeFollower();
                _env[c, b].SetTimes(2.0, 80.0, _sampleRate);
            }
        }
    }

    public IAudioEffect Clone() => new MultibandCompressorEffect
    {
        Enabled = Enabled, Depth = Depth, HighBoostDb = HighBoostDb
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        var depth = (float)Math.Clamp(Depth, 0, 1);
        var highBoost = (float)AudioMath.Db2Lin(HighBoostDb);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];

                // Split into three bands (spectral subtraction keeps the sum coherent).
                var low = (float)_lpState[c].Process(_lp, dry);
                var high = (float)_hpState[c].Process(_hp, dry);
                var mid = dry - low - high;

                low = BandGain(low, _env[c, 0]) * low;
                mid = BandGain(mid, _env[c, 1]) * mid;
                high = BandGain(high, _env[c, 2]) * high * highBoost;

                var wet = low + mid + high;
                buffer[i + c] = dry * (1 - depth) + wet * depth;
            }
        }
    }

    // The OTT gain for one band: downward compression above the threshold, upward compression
    // (a lift toward the threshold) below it — the two together "inflate" the band.
    private static float BandGain(float sample, EnvelopeFollower follower)
    {
        var rect = sample < 0 ? -sample : sample;
        var env = follower.Process(rect);
        var levelDb = AudioMath.Lin2Db(env);

        double gainDb;
        if (levelDb > ThresholdDb)
            gainDb = -(levelDb - ThresholdDb) * (1.0 - 1.0 / DownRatio);
        else
            gainDb = Math.Min(MaxUpwardDb, (ThresholdDb - levelDb) * (1.0 - 1.0 / UpRatio));

        return (float)AudioMath.Db2Lin(gainDb);
    }
}
