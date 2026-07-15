using System;
using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio;

/// <summary>Offline loudness / true-peak analysis of interleaved float PCM.</summary>
public readonly record struct LoudnessReport(
    float IntegratedLufs,
    float ShortTermMaxLufs,
    float MomentaryMaxLufs,
    float TruePeakDbTp,
    float SamplePeakDbFs,
    bool WithinTarget,
    string Summary,
    float LoudnessRangeLu = float.NaN);

/// <summary>Streaming analyser that wraps <see cref="LoudnessMeter"/> + <see cref="TruePeakMeter"/>.</summary>
public sealed class LoudnessAnalyzer
{
    private readonly LoudnessMeter _lufs = new();
    private readonly TruePeakMeter _tp = new();
    private float _samplePeak;
    private float _shortMax = float.NegativeInfinity;
    private float _momMax = float.NegativeInfinity;

    public void Prepare(AudioFormat format)
    {
        _lufs.Prepare(format);
        _tp.Prepare(format);
        _samplePeak = 0;
        _shortMax = _momMax = float.NegativeInfinity;
    }

    public void Reset()
    {
        _lufs.Reset();
        _tp.Reset();
        _samplePeak = 0;
        _shortMax = _momMax = float.NegativeInfinity;
    }

    public void Process(ReadOnlySpan<float> buffer)
    {
        _lufs.Process(buffer);
        _tp.Process(buffer);
        for (var i = 0; i < buffer.Length; i++)
        {
            var a = MathF.Abs(buffer[i]);
            if (a > _samplePeak) _samplePeak = a;
        }
        if (_lufs.ShortTermLufs > _shortMax) _shortMax = _lufs.ShortTermLufs;
        if (_lufs.MomentaryLufs > _momMax) _momMax = _lufs.MomentaryLufs;
    }

    public LoudnessReport Finish(double? targetLufs = null, double? targetTruePeakDbTp = null)
    {
        _lufs.RefreshDelivery();
        var integrated = _lufs.IntegratedLufs;
        var tp = _tp.MaxDbTp;
        var sampleDb = TruePeakMeter.ToDbTp(_samplePeak);
        var lra = _lufs.LoudnessRangeLu;

        // Short material often never fills the absolute gate → −∞ integrated.
        // WithinTarget then only checks true-peak (when a TP target is given).
        var hasIntegrated = !float.IsNegativeInfinity(integrated);
        var within = true;
        if (targetLufs is { } tl && hasIntegrated)
            within &= Math.Abs(integrated - tl) <= 1.0; // ±1 LU tolerance
        if (targetTruePeakDbTp is { } ttp)
            within &= tp <= ttp + 0.05f;
        // No usable loudness + no TP target → leave as OK (nothing to fail against).
        if (!hasIntegrated && targetLufs is not null && targetTruePeakDbTp is null)
            within = false;

        var lufsText = hasIntegrated ? integrated.ToString("0.0") : "−∞";
        var tpText = tp.ToString("0.00");
        var sampleText = sampleDb.ToString("0.00");
        var lraText = float.IsNaN(lra) ? "n/a" : lra.ToString("0.0");
        var flag = within ? "OK" : "OUT OF TARGET";
        if (!hasIntegrated && targetLufs is not null)
            flag = within ? "OK (short — LUFS n/a)" : "OUT OF TARGET (short — LUFS n/a)";
        var summary =
            $"Integrated {lufsText} LUFS · LRA {lraText} LU · True peak {tpText} dBTP · Sample peak {sampleText} dBFS · {flag}";
        return new LoudnessReport(integrated, _shortMax, _momMax, tp, sampleDb, within, summary, lra);
    }

    /// <summary>Analyse an entire interleaved buffer in one shot.</summary>
    public static LoudnessReport Analyze(ReadOnlySpan<float> interleaved, AudioFormat format,
        double? targetLufs = null, double? targetTruePeakDbTp = null)
    {
        var a = new LoudnessAnalyzer();
        a.Prepare(format);
        // Frame-aligned chunks so 5.1/7.1 channel weights stay on the correct channels.
        var ch = format.Channels < 1 ? 1 : format.Channels;
        var blockSamples = 2048 * ch;
        for (var i = 0; i < interleaved.Length; i += blockSamples)
        {
            var len = Math.Min(blockSamples, interleaved.Length - i);
            // Drop a trailing incomplete frame rather than misaligning channel indices.
            len -= len % ch;
            if (len <= 0) break;
            a.Process(interleaved.Slice(i, len));
        }
        return a.Finish(targetLufs, targetTruePeakDbTp);
    }
}
