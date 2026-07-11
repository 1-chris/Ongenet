using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio.Files;

/// <summary>Sparse Constant-Q transform from qm-dsp <c>ConstantQ</c>.</summary>
internal sealed class QueenMaryConstantQ
{
    private readonly double _sampleRate;
    private readonly double _fMin;
    private readonly double _fMax;
    private readonly int _binsPerOctave;
    private readonly double _dQ;
    private readonly double _cqThresh;
    private readonly int _fftLength;
    private readonly int _k;

    private readonly List<int> _sparseIs = new();
    private readonly List<int> _sparseJs = new();
    private readonly List<double> _sparseReal = new();
    private readonly List<double> _sparseImag = new();
    private bool _kernelReady;

    public QueenMaryConstantQ(double sampleRate, double fMin, double fMax, int binsPerOctave, double cqThresh)
    {
        _sampleRate = sampleRate;
        _fMin = fMin;
        _fMax = fMax;
        _binsPerOctave = binsPerOctave;
        _cqThresh = cqThresh;
        _dQ = 1.0 / (Math.Pow(2.0, 1.0 / binsPerOctave) - 1.0);
        _k = (int)Math.Ceiling(binsPerOctave * Math.Log(fMax / fMin, 2.0));
        _fftLength = QueenMaryMath.NextPowerOfTwo((int)Math.Ceiling(_dQ * sampleRate / fMin));
    }

    public int FftLength => _fftLength;
    public int K => _k;
    public int Hop => _fftLength / 8;

    public void EnsureKernel()
    {
        if (_kernelReady) return;
        BuildSparseKernel();
        _kernelReady = true;
    }

    public void Process(double[] fftRe, double[] fftIm, double[] cqRe, double[] cqIm)
    {
        EnsureKernel();
        Array.Clear(cqRe, 0, _k);
        Array.Clear(cqIm, 0, _k);

        for (var i = 0; i < _sparseReal.Count; i++)
        {
            var row = _sparseJs[i];
            var col = _sparseIs[i];
            if (col == 0) continue;
            var r1 = _sparseReal[i];
            var i1 = _sparseImag[i];
            var r2 = fftRe[_fftLength - col];
            var i2 = fftIm[_fftLength - col];
            cqRe[row] += r1 * r2 - i1 * i2;
            cqIm[row] += r1 * i2 + i1 * r2;
        }
    }

    private void BuildSparseKernel()
    {
        var squareThreshold = _cqThresh * _cqThresh;
        var windowRe = new double[_fftLength];
        var windowIm = new double[_fftLength];
        var transfRe = new double[_fftLength];
        var transfIm = new double[_fftLength];

        for (var j = _k - 1; j >= 0; j--)
        {
            Array.Clear(windowRe);
            Array.Clear(windowIm);

            var samplesPerCycle = _sampleRate / (_fMin * Math.Pow(2.0, (double)j / _binsPerOctave));
            var windowLength = (int)Math.Ceiling(_dQ * samplesPerCycle);
            var origin = _fftLength / 2 - windowLength / 2;

            for (var i = 0; i < windowLength; i++)
            {
                var angle = 2.0 * Math.PI * i / samplesPerCycle;
                windowRe[origin + i] = Math.Cos(angle);
                windowIm[origin + i] = Math.Sin(angle);
            }

            ApplyHamming(windowRe, origin, windowLength);
            ApplyHamming(windowIm, origin, windowLength);
            for (var i = 0; i < windowLength; i++)
            {
                windowRe[origin + i] /= windowLength;
                windowIm[origin + i] /= windowLength;
            }

            FftShift(windowRe);
            FftShift(windowIm);
            Array.Copy(windowRe, transfRe, _fftLength);
            Array.Copy(windowIm, transfIm, _fftLength);
            Fft.Forward(transfRe, transfIm);

            for (var i = 0; i < _fftLength; i++)
            {
                var mag = transfRe[i] * transfRe[i] + transfIm[i] * transfIm[i];
                if (mag <= squareThreshold) continue;
                _sparseIs.Add(i);
                _sparseJs.Add(j);
                _sparseReal.Add(transfRe[i] / _fftLength);
                _sparseImag.Add(-transfIm[i] / _fftLength);
            }
        }
    }

    private static void ApplyHamming(double[] data, int origin, int length)
    {
        if (length <= 1) return;
        for (var i = 0; i < length; i++)
            data[origin + i] *= 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / length);
    }

    private static void FftShift(double[] data)
    {
        var hs = data.Length / 2;
        for (var i = 0; i < hs; i++)
            (data[i], data[i + hs]) = (data[i + hs], data[i]);
    }
}
