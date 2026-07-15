using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// ITU-R BS.1770-4 K-weighted loudness meter: momentary (400 ms), short-term (3 s),
/// gated integrated LUFS, and EBU R128 Loudness Range (LRA). Supports stereo and
/// multichannel (5.1 / 7.1) channel weights. Allocation-free after Prepare.
/// Integrated LUFS / LRA are recomputed at UI cadence (~10 Hz), not every audio block.
/// </summary>
public sealed class LoudnessMeter
{
    private const double AbsoluteGateLufs = -70.0;
    private const double RelativeGateLu = -10.0;
    private const double MomentarySeconds = 0.4;
    private const double ShortTermSeconds = 3.0;
    private const double BlockSeconds = 0.4;
    private const double BlockHopSeconds = 0.1; // 75% overlap
    private const double LraAbsoluteGateLufs = -70.0;
    private const double LraRelativeGateLu = -20.0;
    private const double LraLowPercentile = 0.10;
    private const double LraHighPercentile = 0.95;
    private const double HeavyUpdateSeconds = 0.1;
    private const int HistoryCapacity = 65536;

    // BS.1770-4 Table 3 weights for SMPTE / film channel order L R C LFE Ls Rs [Lb Rb].
    private static readonly double[] Weights51 = { 1.0, 1.0, 1.0, 0.0, 1.41, 1.41 };
    private static readonly double[] Weights71 = { 1.0, 1.0, 1.0, 0.0, 1.41, 1.41, 1.41, 1.41 };

    private int _channels = 2;
    private double _sampleRate = 48000;
    private int _momentaryFrames;
    private int _shortTermFrames;
    private int _blockFrames;
    private int _hopFrames;
    private int _heavyUpdateFrames;
    private double[] _weights = { 1.0, 1.0 };

    // K-weighting stages (BS.1770 published 48 kHz prototypes, bilinear for other rates).
    private BiquadCoefficients _shelf = BiquadCoefficients.Identity;
    private BiquadCoefficients _hp = BiquadCoefficients.Identity;
    private Biquad[] _bqShelf = Array.Empty<Biquad>();
    private Biquad[] _bqHp = Array.Empty<Biquad>();

    private float[] _momSq = Array.Empty<float>(); // frames * channels (weighted mean-square rings are summed)
    private float[] _stSq = Array.Empty<float>();
    private int _momWrite, _momCount;
    private int _stWrite, _stCount;
    private double _momSum;
    private double _stSum;

    // True overlapping 400 ms windows: ring of per-sample weighted channel-sum-of-squares.
    private double[] _blockRing = Array.Empty<double>();
    private int _blockRingWrite;
    private int _blockRingCount;
    private double _blockRingSum;
    private int _samplesUntilHop;
    private double[]? _gatingBlocks;
    private int _gatingCount;

    // Short-term mean-square history for LRA (energy domain; LUFS conversion only at Finish).
    private double[]? _shortTermMsHistory;
    private int _shortTermHistoryCount;
    private int _shortTermHistoryHop;
    private float[]? _lraScratch;
    private double[]? _lraMsScratch;
    private int _samplesUntilHeavy;

    private float _momentary = float.NegativeInfinity;
    private float _shortTerm = float.NegativeInfinity;
    private float _integrated = float.NegativeInfinity;
    private float _lra = float.NaN;

    public float MomentaryLufs => _momentary;
    public float ShortTermLufs => _shortTerm;
    public float IntegratedLufs => _integrated;
    /// <summary>EBU R128 Loudness Range in LU. NaN until enough short-term history exists.</summary>
    public float LoudnessRangeLu => _lra;

    /// <summary>Approximate retained history capacity in bytes once histories are allocated.</summary>
    public static int EstimatedHistoryBytes =>
        HistoryCapacity * (sizeof(double) * 3 + sizeof(float)); // gating + ST + LRA scratch*2 (approx)

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 48000;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _weights = ChannelWeights(_channels);
        _momentaryFrames = Math.Max(1, (int)(_sampleRate * MomentarySeconds));
        _shortTermFrames = Math.Max(1, (int)(_sampleRate * ShortTermSeconds));
        _blockFrames = Math.Max(1, (int)(_sampleRate * BlockSeconds));
        _hopFrames = Math.Max(1, (int)(_sampleRate * BlockHopSeconds));
        _heavyUpdateFrames = Math.Max(1, (int)(_sampleRate * HeavyUpdateSeconds));

        BuildKFilters(_sampleRate);
        EnsureFilters(_channels);
        EnsureRing(ref _momSq, _momentaryFrames);
        EnsureRing(ref _stSq, _shortTermFrames);
        if (_blockRing.Length != _blockFrames)
            _blockRing = new double[_blockFrames];
        // Prepare is the lifecycle boundary between lightweight structural clones and live DSP.
        // Allocate histories here, never at the first 400 ms gating hop on the audio thread.
        EnsureHistories();
        Reset();
    }

    public void Reset()
    {
        Array.Clear(_momSq);
        Array.Clear(_stSq);
        Array.Clear(_blockRing);
        _momWrite = _momCount = 0;
        _stWrite = _stCount = 0;
        _momSum = _stSum = 0;
        _blockRingWrite = 0;
        _blockRingCount = 0;
        _blockRingSum = 0;
        _samplesUntilHop = _hopFrames;
        _gatingCount = 0;
        _shortTermHistoryCount = 0;
        _shortTermHistoryHop = _hopFrames;
        _samplesUntilHeavy = _heavyUpdateFrames;
        _momentary = _shortTerm = _integrated = float.NegativeInfinity;
        _lra = float.NaN;
        for (var i = 0; i < _bqShelf.Length; i++)
        {
            _bqShelf[i].Reset();
            _bqHp[i].Reset();
        }
    }

    public void Process(ReadOnlySpan<float> buffer)
    {
        var ch = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / ch;
        if (frames == 0 || _momentaryFrames == 0) return;

        for (var f = 0; f < frames; f++)
        {
            double weightedSumSq = 0;
            for (var c = 0; c < ch; c++)
            {
                var w = c < _weights.Length ? _weights[c] : 1.0;
                if (w <= 0) continue;
                var x = buffer[f * ch + c];
                var kx = (float)_bqHp[c].Process(in _hp, _bqShelf[c].Process(in _shelf, x));
                weightedSumSq += w * (kx * kx);
            }

            PushMomentary((float)weightedSumSq);
            PushShortTerm((float)weightedSumSq);
            PushGatingSample(weightedSumSq);
            PushShortTermHistory();
        }

        _momentary = MeanSquareToLufs(_momSum, _momCount);
        _shortTerm = MeanSquareToLufs(_stSum, _stCount);

        _samplesUntilHeavy -= frames;
        if (_samplesUntilHeavy <= 0)
        {
            _samplesUntilHeavy = _heavyUpdateFrames;
            _integrated = ComputeIntegrated();
            _lra = ComputeLra();
        }
    }

    /// <summary>Forces integrated LUFS / LRA recompute (e.g. UI reset or export finalize).</summary>
    public void RefreshDelivery()
    {
        _integrated = ComputeIntegrated();
        _lra = ComputeLra();
        _samplesUntilHeavy = _heavyUpdateFrames;
    }

    private void EnsureHistories()
    {
        _gatingBlocks ??= new double[HistoryCapacity];
        _shortTermMsHistory ??= new double[HistoryCapacity];
        _lraScratch ??= new float[HistoryCapacity];
        _lraMsScratch ??= new double[HistoryCapacity];
    }

    private void PushMomentary(float sumSq)
    {
        if (_momCount == _momentaryFrames)
            _momSum -= _momSq[_momWrite];
        else _momCount++;

        _momSq[_momWrite] = sumSq;
        _momSum += sumSq;
        _momWrite = (_momWrite + 1) % _momentaryFrames;
    }

    private void PushShortTerm(float sumSq)
    {
        if (_stCount == _shortTermFrames)
            _stSum -= _stSq[_stWrite];
        else _stCount++;

        _stSq[_stWrite] = sumSq;
        _stSum += sumSq;
        _stWrite = (_stWrite + 1) % _shortTermFrames;
    }

    private void PushGatingSample(double channelSumSq)
    {
        if (_blockRingCount == _blockFrames)
            _blockRingSum -= _blockRing[_blockRingWrite];
        else _blockRingCount++;

        _blockRing[_blockRingWrite] = channelSumSq;
        _blockRingSum += channelSumSq;
        _blockRingWrite = (_blockRingWrite + 1) % _blockFrames;

        _samplesUntilHop--;
        if (_samplesUntilHop > 0) return;
        _samplesUntilHop = _hopFrames;

        if (_blockRingCount < _blockFrames) return;
        EnsureHistories();
        var meanSq = _blockRingSum / _blockFrames;
        if (_gatingCount < _gatingBlocks!.Length)
            _gatingBlocks[_gatingCount++] = meanSq;
    }

    private void PushShortTermHistory()
    {
        _shortTermHistoryHop--;
        if (_shortTermHistoryHop > 0) return;
        _shortTermHistoryHop = _hopFrames;
        if (_stCount < _shortTermFrames) return;
        EnsureHistories();
        if (_shortTermHistoryCount >= _shortTermMsHistory!.Length) return;
        // Store mean-square energy (not LUFS) so relative gating is energy-domain compliant.
        var meanSq = _stSum / _stCount;
        if (meanSq > 1e-20)
            _shortTermMsHistory[_shortTermHistoryCount++] = meanSq;
    }

    private float ComputeIntegrated()
    {
        if (_gatingBlocks is null || _gatingCount == 0) return float.NegativeInfinity;

        var absThresh = Math.Pow(10.0, (AbsoluteGateLufs + 0.691) / 10.0);
        double sum = 0;
        var n = 0;
        for (var i = 0; i < _gatingCount; i++)
        {
            var z = _gatingBlocks[i];
            if (z >= absThresh) { sum += z; n++; }
        }
        if (n == 0) return float.NegativeInfinity;

        var gatedMean = sum / n;
        var relThresh = gatedMean * Math.Pow(10.0, RelativeGateLu / 10.0);

        sum = 0;
        n = 0;
        for (var i = 0; i < _gatingCount; i++)
        {
            var z = _gatingBlocks[i];
            if (z >= absThresh && z >= relThresh) { sum += z; n++; }
        }
        if (n == 0) return float.NegativeInfinity;

        var mean = sum / n;
        if (mean <= 1e-20) return float.NegativeInfinity;
        return (float)(-0.691 + 10.0 * Math.Log10(mean));
    }

    private float ComputeLra()
    {
        if (_shortTermMsHistory is null || _lraScratch is null || _lraMsScratch is null
            || _shortTermHistoryCount < 10) return float.NaN;

        // EBU R128: absolute gate at −70 LUFS on energy, then relative −20 LU from
        // energy-mean of absolutely gated blocks (not arithmetic mean of LUFS).
        var absThresh = Math.Pow(10.0, (LraAbsoluteGateLufs + 0.691) / 10.0);
        var n = 0;
        double absSum = 0;
        for (var i = 0; i < _shortTermHistoryCount; i++)
        {
            var z = _shortTermMsHistory[i];
            if (z >= absThresh)
            {
                _lraMsScratch[n++] = z;
                absSum += z;
            }
        }
        if (n < 10) return float.NaN;

        var absMean = absSum / n;
        var relThresh = absMean * Math.Pow(10.0, LraRelativeGateLu / 10.0);

        var g = 0;
        for (var i = 0; i < n; i++)
        {
            var z = _lraMsScratch[i];
            if (z >= relThresh)
                _lraScratch[g++] = (float)(-0.691 + 10.0 * Math.Log10(z));
        }
        if (g < 10) return float.NaN;

        Array.Sort(_lraScratch, 0, g);
        var low = Percentile(_lraScratch, g, LraLowPercentile);
        var high = Percentile(_lraScratch, g, LraHighPercentile);
        return high - low;
    }

    private static float Percentile(float[] sorted, int count, double p)
    {
        if (count <= 1) return sorted[0];
        var idx = p * (count - 1);
        var i = (int)idx;
        var frac = (float)(idx - i);
        if (i >= count - 1) return sorted[count - 1];
        return sorted[i] * (1 - frac) + sorted[i + 1] * frac;
    }

    private static float MeanSquareToLufs(double sumSq, int frames)
    {
        if (frames <= 0) return float.NegativeInfinity;
        var mean = sumSq / frames;
        if (mean <= 1e-20) return float.NegativeInfinity;
        return (float)(-0.691 + 10.0 * Math.Log10(mean));
    }

    private static double[] ChannelWeights(int channels) => channels switch
    {
        >= 8 => Weights71,
        >= 6 => Weights51,
        1 => new[] { 1.0 },
        _ => new[] { 1.0, 1.0 }
    };

    private void EnsureFilters(int channels)
    {
        if (_bqShelf.Length == channels) return;
        _bqShelf = new Biquad[channels];
        _bqHp = new Biquad[channels];
        for (var i = 0; i < channels; i++)
        {
            _bqShelf[i].Reset();
            _bqHp[i].Reset();
        }
    }

    private void BuildKFilters(double sr)
    {
        if (Math.Abs(sr - 48000.0) < 1.0)
        {
            _shelf = new BiquadCoefficients(
                1.53512485958697, -2.69169618940638, 1.19839281085285,
                -1.69065929318241, 0.73248077421585);
            _hp = new BiquadCoefficients(
                1.0, -2.0, 1.0,
                -1.99004745483398, 0.99007225036621);
            return;
        }

        _shelf = BiquadCoefficients.ComputeEq(
            Ongenet.Core.Audio.Effects.EqBandType.HighShelf,
            1681.974450955533, 0.7071752369554196, 3.999843853973347, sr);
        _hp = BiquadCoefficients.Compute(
            Ongenet.Core.Audio.Effects.FilterMode.HighPass,
            38.13547087602444, 0.5003270373239323, sr);
    }

    private static void EnsureRing(ref float[] buf, int needed)
    {
        if (buf.Length != needed) buf = new float[needed];
    }
}
