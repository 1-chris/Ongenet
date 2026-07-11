using System;
using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Onset detection function modeled on qm-dsp <c>DetectionFunction</c> (Complex Spectral Difference).
/// </summary>
internal sealed class QueenMaryDetectionFunction
{
    private readonly int _frameLength;
    private readonly int _halfLength;
    private readonly double[] _window;
    private readonly double[] _magHistory;
    private readonly double[] _phaseHistory;
    private readonly double[] _phaseHistoryOld;
    private readonly double[] _re;
    private readonly double[] _im;

    public QueenMaryDetectionFunction(int frameLength)
    {
        _frameLength = frameLength;
        _halfLength = frameLength / 2 + 1;
        _window = BuildHann(frameLength);
        _magHistory = new double[_halfLength];
        _phaseHistory = new double[_halfLength];
        _phaseHistoryOld = new double[_halfLength];
        _re = new double[frameLength];
        _im = new double[frameLength];
    }

    public double ProcessTimeDomain(ReadOnlySpan<float> samples)
    {
        var windowed = new double[_frameLength];
        var count = Math.Min(samples.Length, _frameLength);
        for (var i = 0; i < count; i++)
            windowed[i] = samples[i] * _window[i];

        Array.Clear(_re);
        Array.Clear(_im);
        var hs = _frameLength / 2;
        for (var i = 0; i < hs; i++)
            _re[i] = windowed[i + hs];
        for (var i = 0; i < hs; i++)
            _re[i + hs] = windowed[i];

        Fft.Forward(_re, _im);

        var magnitude = new double[_halfLength];
        var phase = new double[_halfLength];
        for (var k = 0; k < _halfLength; k++)
        {
            magnitude[k] = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
            phase[k] = Math.Atan2(_im[k], _re[k]);
        }

        return ComplexSpectralDifference(magnitude, phase);
    }

    private double ComplexSpectralDifference(double[] magnitude, double[] phase)
    {
        var val = 0.0;
        for (var i = 0; i < _halfLength; i++)
        {
            var dev = QueenMaryMath.PrincArg(phase[i] - 2.0 * _phaseHistory[i] + _phaseHistoryOld[i]);
            var cos = Math.Cos(dev);
            var sin = Math.Sin(dev);
            var real = _magHistory[i] - magnitude[i] * cos;
            var imag = -magnitude[i] * sin;
            val += Math.Sqrt(real * real + imag * imag);

            _phaseHistoryOld[i] = _phaseHistory[i];
            _phaseHistory[i] = phase[i];
            _magHistory[i] = magnitude[i];
        }

        return val;
    }

    private static double[] BuildHann(int size)
    {
        var window = new double[size];
        if (size <= 1)
        {
            window[0] = 1.0;
            return window;
        }

        for (var i = 0; i < size; i++)
            window[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (size - 1));
        return window;
    }
}
