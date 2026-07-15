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

    public double LowCrossoverHz { get; set; } = 200.0;
    public double HighCrossoverHz { get; set; } = 2500.0;
    public double ThresholdDb { get; set; } = -30.0;
    public double DownRatio { get; set; } = 4.0;
    public double UpRatio { get; set; } = 3.0;
    public double MaxUpwardDb { get; set; } = 12.0;
    public bool SoloLow { get; set; }
    public bool SoloMid { get; set; }
    public bool SoloHigh { get; set; }
    public bool MuteLow { get; set; }
    public bool MuteMid { get; set; }
    public bool MuteHigh { get; set; }

    public double LowEnergy { get; private set; }
    public double MidEnergy { get; private set; }
    public double HighEnergy { get; private set; }
    public double LowGainReductionDb { get; private set; }
    public double MidGainReductionDb { get; private set; }
    public double HighGainReductionDb { get; private set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;

    private BiquadCoefficients _lp = BiquadCoefficients.Identity;
    private BiquadCoefficients _hp = BiquadCoefficients.Identity;

    private Biquad[] _lpState = Array.Empty<Biquad>();
    private Biquad[] _hpState = Array.Empty<Biquad>();
    private EnvelopeFollower[,] _env = new EnvelopeFollower[0, 0];
    private int _lastPresetIndex = -1;
    private double _lastLowCross = -1;
    private double _lastHighCross = -1;

    public string Name => "Multiband (OTT)";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Preset",
            Array.ConvertAll(MasteringPresetBank.MultibandPresets, p => p.Name),
            () => MasteringPresetIndex, v => MasteringPresetIndex = v,
            Array.ConvertAll(MasteringPresetBank.MultibandPresets, p => p.Description)),
        new FloatParameter("Depth", 0.0, 1.0, () => Depth, v => Depth = v),
        new FloatParameter("High Boost", 0.0, 9.0, () => HighBoostDb, v => HighBoostDb = v, "0.#", "dB"),
        new FloatParameter("Low Cross", 40.0, 800.0, () => LowCrossoverHz, v => LowCrossoverHz = v, "0", "Hz", 2.0)
            { Group = "Crossovers" },
        new FloatParameter("High Cross", 800.0, 8000.0, () => HighCrossoverHz, v => HighCrossoverHz = v, "0", "Hz", 2.0)
            { Group = "Crossovers" },
        new FloatParameter("Threshold", -60.0, 0.0, () => ThresholdDb, v => ThresholdDb = v, "0.#", "dB")
            { Group = "Dynamics" },
        new FloatParameter("Down Ratio", 1.0, 20.0, () => DownRatio, v => DownRatio = v, "0.#")
            { Group = "Dynamics" },
        new FloatParameter("Up Ratio", 1.0, 20.0, () => UpRatio, v => UpRatio = v, "0.#")
            { Group = "Dynamics" },
        new FloatParameter("Max Upward", 0.0, 24.0, () => MaxUpwardDb, v => MaxUpwardDb = v, "0.#", "dB")
            { Group = "Dynamics" },
        new BoolParameter("Solo Low", () => SoloLow, v => SoloLow = v) { Group = "Band Audition" },
        new BoolParameter("Solo Mid", () => SoloMid, v => SoloMid = v) { Group = "Band Audition" },
        new BoolParameter("Solo High", () => SoloHigh, v => SoloHigh = v) { Group = "Band Audition" },
        new BoolParameter("Mute Low", () => MuteLow, v => MuteLow = v) { Group = "Band Audition" },
        new BoolParameter("Mute Mid", () => MuteMid, v => MuteMid = v) { Group = "Band Audition" },
        new BoolParameter("Mute High", () => MuteHigh, v => MuteHigh = v) { Group = "Band Audition" }
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        RebuildFilters(force: true);

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

        _lpState = lpState;
        _hpState = hpState;
        _env = env;
    }

    private void RebuildFilters(bool force = false)
    {
        var low = Math.Clamp(LowCrossoverHz, 40.0, 800.0);
        var high = Math.Clamp(HighCrossoverHz, Math.Max(low + 50.0, 800.0), 8000.0);
        LowCrossoverHz = low;
        HighCrossoverHz = high;
        if (!force && Math.Abs(low - _lastLowCross) < 0.01 && Math.Abs(high - _lastHighCross) < 0.01)
            return;
        _lastLowCross = low;
        _lastHighCross = high;
        _lp = BiquadCoefficients.Compute(FilterMode.LowPass, low, 0.707, _sampleRate);
        _hp = BiquadCoefficients.Compute(FilterMode.HighPass, high, 0.707, _sampleRate);
    }

    public IAudioEffect Clone()
    {
        var c = new MultibandCompressorEffect
        {
            Enabled = Enabled, Depth = Depth, HighBoostDb = HighBoostDb,
            MasteringPresetIndex = MasteringPresetIndex,
            LowCrossoverHz = LowCrossoverHz, HighCrossoverHz = HighCrossoverHz,
            ThresholdDb = ThresholdDb, DownRatio = DownRatio, UpRatio = UpRatio,
            MaxUpwardDb = MaxUpwardDb,
            SoloLow = SoloLow, SoloMid = SoloMid, SoloHigh = SoloHigh,
            MuteLow = MuteLow, MuteMid = MuteMid, MuteHigh = MuteHigh
        };
        c._lastPresetIndex = MasteringPresetIndex;
        return c;
    }

    public void Process(Span<float> buffer)
    {
        ApplyPresetIfChanged();
        RebuildFilters();
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
        var threshold = ThresholdDb;
        var downRatio = Math.Max(1.0, DownRatio);
        var upRatio = Math.Max(1.0, UpRatio);
        var maxUp = MaxUpwardDb;
        var anySolo = SoloLow || SoloMid || SoloHigh;
        var hearLow = !MuteLow && (!anySolo || SoloLow);
        var hearMid = !MuteMid && (!anySolo || SoloMid);
        var hearHigh = !MuteHigh && (!anySolo || SoloHigh);

        var frames = buffer.Length / channels;
        double lowEnergy = 0, midEnergy = 0, highEnergy = 0;
        double lowGr = 0, midGr = 0, highGr = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];

                var low = (float)lpState[c].Process(lp, dry);
                var high = (float)hpState[c].Process(hp, dry);
                var mid = dry - low - high;

                var envLow = env[c, 0];
                var envMid = env[c, 1];
                var envHigh = env[c, 2];
                if (envLow is null || envMid is null || envHigh is null) continue;

                low = BandGain(low, envLow, threshold, downRatio, upRatio, maxUp, out var lowGainDb) * low;
                mid = BandGain(mid, envMid, threshold, downRatio, upRatio, maxUp, out var midGainDb) * mid;
                high = BandGain(high, envHigh, threshold, downRatio, upRatio, maxUp, out var highGainDb) * high * highBoost;
                lowEnergy += low * low;
                midEnergy += mid * mid;
                highEnergy += high * high;
                lowGr = Math.Min(lowGr, lowGainDb);
                midGr = Math.Min(midGr, midGainDb);
                highGr = Math.Min(highGr, highGainDb);

                var wet = (hearLow ? low : 0f) + (hearMid ? mid : 0f) + (hearHigh ? high : 0f);
                buffer[i + c] = dry * (1 - depth) + wet * depth;
            }
        }
        var count = Math.Max(1, frames * channels);
        LowEnergy = LowEnergy * 0.75 + Math.Sqrt(lowEnergy / count) * 0.25;
        MidEnergy = MidEnergy * 0.75 + Math.Sqrt(midEnergy / count) * 0.25;
        HighEnergy = HighEnergy * 0.75 + Math.Sqrt(highEnergy / count) * 0.25;
        LowGainReductionDb = lowGr;
        MidGainReductionDb = midGr;
        HighGainReductionDb = highGr;
    }

    private static float BandGain(float sample, EnvelopeFollower follower,
        double thresholdDb, double downRatio, double upRatio, double maxUpwardDb, out double gainDb)
    {
        var rect = sample < 0 ? -sample : sample;
        var env = follower.Process(rect);
        var levelDb = AudioMath.Lin2Db(env);

        if (levelDb > thresholdDb)
            gainDb = -(levelDb - thresholdDb) * (1.0 - 1.0 / downRatio);
        else
            gainDb = Math.Min(maxUpwardDb, (thresholdDb - levelDb) * (1.0 - 1.0 / upRatio));

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
