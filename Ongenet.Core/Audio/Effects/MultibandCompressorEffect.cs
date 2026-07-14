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

    /// <summary>Factory mastering macro preset index.</summary>
    public int MasteringPresetIndex { get; set; }

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
    private int _lastPresetIndex;

    public string Name => "Multiband (OTT)";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Preset",
            Array.ConvertAll(MasteringPresetBank.MultibandPresets, p => p.Name),
            () => MasteringPresetIndex, v => MasteringPresetIndex = v),
        new FloatParameter("Depth", 0.0, 1.0, () => Depth, v => Depth = v),
        new FloatParameter("High Boost", 0.0, 9.0, () => HighBoostDb, v => HighBoostDb = v, "0.#", "dB")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;

        var lp = BiquadCoefficients.Compute(FilterMode.LowPass, LowCrossHz, 0.707, _sampleRate);
        var hp = BiquadCoefficients.Compute(FilterMode.HighPass, HighCrossHz, 0.707, _sampleRate);

        var lpState = new Biquad[_channels];
        var hpState = new Biquad[_channels];
        var env = new EnvelopeFollower[_channels, 3];
        for (var c = 0; c < _channels; c++)
        {
            lpState[c].Reset();
            hpState[c].Reset();
            for (var b = 0; b < 3; b++)
            {
                env[c, b] = new EnvelopeFollower();
                env[c, b].SetTimes(2.0, 80.0, _sampleRate);
            }
        }

        // Publish fully-built state with single assignments — RebuildTracks can call Prepare from the UI
        // thread while Process runs on the audio thread (e.g. after "Render clip to new track").
        _lp = lp;
        _hp = hp;
        _lpState = lpState;
        _hpState = hpState;
        _env = env;
    }

    public IAudioEffect Clone()
    {
        var c = new MultibandCompressorEffect
        {
            Enabled = Enabled, Depth = Depth, HighBoostDb = HighBoostDb, MasteringPresetIndex = MasteringPresetIndex
        };
        c._lastPresetIndex = MasteringPresetIndex;
        return c;
    }

    public void Process(Span<float> buffer)
    {
        ApplyPresetIfChanged();
        var lpState = _lpState;
        var hpState = _hpState;
        var env = _env;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, lpState.Length);
        if (channels <= 0 || hpState.Length < channels || env.GetLength(0) < channels || env.GetLength(1) < 3)
            return;

        var lp = _lp;
        var hp = _hp;
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
                var low = (float)lpState[c].Process(lp, dry);
                var high = (float)hpState[c].Process(hp, dry);
                var mid = dry - low - high;

                var envLow = env[c, 0];
                var envMid = env[c, 1];
                var envHigh = env[c, 2];
                if (envLow is null || envMid is null || envHigh is null) continue;

                low = BandGain(low, envLow) * low;
                mid = BandGain(mid, envMid) * mid;
                high = BandGain(high, envHigh) * high * highBoost;

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

    private void ApplyPresetIfChanged()
    {
        if (MasteringPresetIndex == _lastPresetIndex) return;
        _lastPresetIndex = MasteringPresetIndex;
        var preset = MasteringPresetBank.GetMultiband(MasteringPresetIndex);
        Depth = preset.Depth;
        HighBoostDb = preset.HighBoostDb;
    }
}
