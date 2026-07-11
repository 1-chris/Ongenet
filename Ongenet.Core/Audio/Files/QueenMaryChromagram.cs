using System;
using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio.Files;

/// <summary>Constant-Q chromagram from qm-dsp <c>Chromagram</c>.</summary>
internal sealed class QueenMaryChromagram
{
    private readonly QueenMaryConstantQ _constantQ;
    private readonly int _binsPerOctave;
    private readonly double[] _chromaData;
    private readonly double[] _fftRe;
    private readonly double[] _fftIm;
    private readonly double[] _cqRe;
    private readonly double[] _cqIm;
    private readonly double[] _hamming;

    public QueenMaryChromagram(double sampleRate, double fMin, double fMax, int binsPerOctave)
    {
        _binsPerOctave = binsPerOctave;
        _constantQ = new QueenMaryConstantQ(sampleRate, fMin, fMax, binsPerOctave, 0.0054);
        _constantQ.EnsureKernel();
        FrameSize = _constantQ.FftLength;
        HopSize = FrameSize; // frameOverlapFactor = 1 in Mixxx key analyzer
        _chromaData = new double[binsPerOctave];
        _fftRe = new double[FrameSize];
        _fftIm = new double[FrameSize];
        _cqRe = new double[_constantQ.K];
        _cqIm = new double[_constantQ.K];
        _hamming = BuildHamming(FrameSize);
    }

    public int FrameSize { get; }
    public int HopSize { get; }

    public double[] Process(double[] data)
    {
        var windowed = new double[FrameSize];
        Array.Copy(data, windowed, Math.Min(data.Length, FrameSize));
        for (var i = 0; i < FrameSize; i++)
            windowed[i] *= _hamming[i];

        FftShift(windowed);
        Array.Copy(windowed, _fftRe, FrameSize);
        Array.Clear(_fftIm);
        Fft.Forward(_fftRe, _fftIm);

        _constantQ.Process(_fftRe, _fftIm, _cqRe, _cqIm);

        Array.Clear(_chromaData);
        var octaves = _constantQ.K / _binsPerOctave;
        for (var octave = 0; octave < octaves; octave++)
        {
            var firstBin = octave * _binsPerOctave;
            for (var i = 0; i < _binsPerOctave; i++)
            {
                var re = _cqRe[firstBin + i];
                var im = _cqIm[firstBin + i];
                _chromaData[i] += Math.Sqrt(re * re + im * im);
            }
        }

        QueenMaryMath.NormalizeUnitMax(_chromaData);
        return _chromaData;
    }

    private static double[] BuildHamming(int size)
    {
        var window = new double[size];
        if (size <= 1)
        {
            window[0] = 1;
            return window;
        }

        for (var i = 0; i < size; i++)
            window[i] = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / size);
        return window;
    }

    private static void FftShift(double[] data)
    {
        var hs = data.Length / 2;
        for (var i = 0; i < hs; i++)
            (data[i], data[i + hs]) = (data[i + hs], data[i]);
    }
}
