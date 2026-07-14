using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects.Spectral;

/// <summary>
/// Loudness split: routes content above and below a level threshold to separate FX chains using a
/// soft envelope gate derived from peak detection.
/// </summary>
public sealed class LoudSplitEffect : SpectralSplitEffectBase
{
    public const string TypeId = "loud_split";

    protected override string GetTypeId() => TypeId;

    public override string Name => "Loud Split";

    public double ThresholdDb { get; set; } = -18.0;
    public double SoftnessDb { get; set; } = 6.0;

    private readonly EnvelopeFollower _follower = new();
    private double _sampleRate = 44100.0;
    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Threshold", -48, 0, () => ThresholdDb, v => ThresholdDb = v, "0.#", "dB"),
        new FloatParameter("Softness", 0.5, 24, () => SoftnessDb, v => SoftnessDb = v, "0.#", "dB")
    };

    protected override void OnPrepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _follower.Reset();
        _follower.SetTimes(1.0, 40.0, _sampleRate);
    }

    public override void Process(Span<float> buffer)
    {
        var channels = Format.Channels < 1 ? 1 : Format.Channels;
        var threshold = AudioMath.Db2Lin(ThresholdDb);
        var knee = Math.Max(0.001, AudioMath.Db2Lin(ThresholdDb + SoftnessDb) - threshold);

        ProcessDualBand(buffer, (_, idx, sample, quiet, loud) =>
        {
            var peak = sample < 0 ? -sample : sample;
            var env = _follower.Process(peak);
            var t = (env - threshold) / knee;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            loud[idx] = sample * (float)t;
            quiet[idx] = sample * (float)(1.0 - t);
        });
    }

    public override IAudioEffect Clone()
    {
        var c = new LoudSplitEffect { ThresholdDb = ThresholdDb, SoftnessDb = SoftnessDb };
        CloneBranchesInto(c);
        return c;
    }
}
