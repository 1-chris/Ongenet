using System;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Key estimator ported from qm-dsp <c>GetKeyMode</c> (Mixxx <c>AnalyzerQueenMaryKey</c>).
/// Returns key index 1–24 (1 = C major, 13 = C minor); 0 = no key.
/// </summary>
internal sealed class QueenMaryGetKeyMode
{
    private const int BinsPerOctave = 36;

    private static readonly double[] MajProfile =
    {
        0.0384, 0.0629, 0.0258, 0.0121, 0.0146, 0.0106, 0.0364, 0.0610, 0.0267,
        0.0126, 0.0121, 0.0086, 0.0364, 0.0623, 0.0279, 0.0275, 0.0414, 0.0186,
        0.0173, 0.0248, 0.0145, 0.0364, 0.0631, 0.0262, 0.0129, 0.0150, 0.0098,
        0.0312, 0.0521, 0.0235, 0.0129, 0.0142, 0.0095, 0.0289, 0.0478, 0.0239
    };

    private static readonly double[] MinProfile =
    {
        0.0375, 0.0682, 0.0299, 0.0119, 0.0138, 0.0093, 0.0296, 0.0543, 0.0257,
        0.0292, 0.0519, 0.0246, 0.0159, 0.0234, 0.0135, 0.0291, 0.0544, 0.0248,
        0.0137, 0.0176, 0.0104, 0.0352, 0.0670, 0.0302, 0.0222, 0.0349, 0.0164,
        0.0174, 0.0297, 0.0166, 0.0222, 0.0401, 0.0202, 0.0175, 0.0270, 0.0146
    };

    private readonly QueenMaryDecimator _decimator;
    private readonly QueenMaryChromagram _chroma;
    private readonly double[] _decimatedBuffer;
    private readonly double[] _chromaBuffer;
    private readonly double[] _meanHpcp;
    private readonly double[] _majCorr;
    private readonly double[] _minCorr;
    private readonly double[] _majProfileNorm;
    private readonly double[] _minProfileNorm;
    private readonly int[] _medianFilterBuffer;
    private readonly int[] _sortedBuffer;

    private readonly int _chromaBufferSize;
    private readonly int _medianWinSize;
    private int _bufferIndex;
    private int _chromaBufferFilling;
    private int _medianBufferFilling;

    public QueenMaryGetKeyMode(int sampleRate, float tuningFrequency = 440f)
    {
        const int decimationFactor = 8;
        const double hpcpAverage = 10.0;
        const double medianAverage = 10.0;
        const int frameOverlapFactor = 1;

        const double centsOffset = -12.0 / BinsPerOctave * 100.0;
        var fMin = QueenMaryPitch.FrequencyForMidi(48, centsOffset, tuningFrequency);
        var fMax = QueenMaryPitch.FrequencyForMidi(96, centsOffset, tuningFrequency);
        var decimatedRate = sampleRate / (double)decimationFactor;

        _chroma = new QueenMaryChromagram(decimatedRate, fMin, fMax, BinsPerOctave);
        ChromaFrameSize = _chroma.FrameSize;
        ChromaHopSize = _chroma.FrameSize / frameOverlapFactor;
        BlockSize = ChromaFrameSize * decimationFactor;
        HopSize = ChromaHopSize * decimationFactor;

        _decimator = new QueenMaryDecimator(BlockSize, decimationFactor);
        _decimatedBuffer = new double[ChromaFrameSize];

        _chromaBufferSize = (int)Math.Ceiling(hpcpAverage * decimatedRate / ChromaFrameSize);
        _medianWinSize = (int)Math.Ceiling(medianAverage * decimatedRate / ChromaFrameSize);
        if (_chromaBufferSize < 1) _chromaBufferSize = 1;
        if (_medianWinSize < 1) _medianWinSize = 1;

        _chromaBuffer = new double[BinsPerOctave * _chromaBufferSize];
        _meanHpcp = new double[BinsPerOctave];
        _majCorr = new double[BinsPerOctave];
        _minCorr = new double[BinsPerOctave];
        _majProfileNorm = NormalizeProfile(MajProfile);
        _minProfileNorm = NormalizeProfile(MinProfile);
        _medianFilterBuffer = new int[_medianWinSize];
        _sortedBuffer = new int[_medianWinSize];
    }

    public int BlockSize { get; }
    public int HopSize { get; }
    public int ChromaFrameSize { get; }
    public int ChromaHopSize { get; }

    public int Process(ReadOnlySpan<float> pcmBlock)
    {
        _decimator.Process(pcmBlock, _decimatedBuffer);
        var chroma = _chroma.Process(_decimatedBuffer);

        for (var j = 0; j < BinsPerOctave; j++)
            _chromaBuffer[_bufferIndex * BinsPerOctave + j] = chroma[j];

        if (++_bufferIndex >= _chromaBufferSize)
            _bufferIndex = 0;
        if (_chromaBufferFilling < _chromaBufferSize)
            _chromaBufferFilling++;

        for (var k = 0; k < BinsPerOctave; k++)
        {
            var sum = 0.0;
            for (var j = 0; j < _chromaBufferFilling; j++)
                sum += _chromaBuffer[k + j * BinsPerOctave];
            _meanHpcp[k] = sum / _chromaBufferFilling;
        }

        var hpcpMean = QueenMaryMath.Mean(_meanHpcp, BinsPerOctave);
        for (var k = 0; k < BinsPerOctave; k++)
            _meanHpcp[k] -= hpcpMean;

        for (var k = 0; k < BinsPerOctave; k++)
        {
            _majCorr[k] = KrumCorr(_meanHpcp, _majProfileNorm, k - 1);
            _minCorr[k] = KrumCorr(_meanHpcp, _minProfileNorm, k - 1);
        }

        var maxMajBin = QueenMaryMath.GetMaxIndex(_majCorr, out var maxMaj);
        var maxMinBin = QueenMaryMath.GetMaxIndex(_minCorr, out var maxMin);
        var maxBin = maxMaj > maxMin ? maxMajBin : maxMinBin + BinsPerOctave;
        var key = maxBin / 3 + 1;

        if (_medianBufferFilling < _medianWinSize)
            _medianBufferFilling++;

        for (var k = 1; k < _medianWinSize; k++)
            _medianFilterBuffer[k - 1] = _medianFilterBuffer[k];
        _medianFilterBuffer[_medianWinSize - 1] = key;

        var sortLength = _medianBufferFilling;
        for (var k = 0; k < sortLength; k++)
            _sortedBuffer[k] = _medianFilterBuffer[_medianWinSize - 1 - k];
        Array.Sort(_sortedBuffer, 0, sortLength);

        var midpoint = (int)Math.Ceiling(sortLength / 2.0);
        if (midpoint <= 0) midpoint = 1;
        return _sortedBuffer[midpoint - 1];
    }

    private static double KrumCorr(double[] data, double[] profile, int shift)
    {
        double num = 0, sum1 = 0, sum2 = 0;
        for (var i = 0; i < BinsPerOctave; i++)
        {
            var k = (i - shift + BinsPerOctave) % BinsPerOctave;
            num += data[i] * profile[k];
            sum1 += data[i] * data[i];
            sum2 += profile[k] * profile[k];
        }

        var den = Math.Sqrt(sum1 * sum2);
        return den > 0 ? num / den : 0;
    }

    private static double[] NormalizeProfile(double[] profile)
    {
        var mean = QueenMaryMath.Mean(profile, profile.Length);
        var norm = new double[profile.Length];
        for (var i = 0; i < profile.Length; i++)
            norm[i] = profile[i] - mean;
        return norm;
    }
}
