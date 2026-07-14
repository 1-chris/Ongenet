using System;
using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio.Effects.Spectral;

/// <summary>
/// Real-time magnitude analysis using <see cref="Fft"/> (same kernel as
/// <see cref="Dsp.SpectralAnalyzer"/>). Used by spectral split devices for band metering.
/// </summary>
internal static class SpectralFftHelper
{
    public static float[] ComputeMagnitudes(ReadOnlySpan<float> mono, int fftSize = 2048)
    {
        if (!Fft.IsPowerOfTwo(fftSize)) fftSize = 2048;
        if (mono.Length < 64) return Array.Empty<float>();

        var n = Math.Min(fftSize, mono.Length);
        var re = new double[n];
        var im = new double[n];
        for (var i = 0; i < n; i++)
        {
            var w = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (n - 1)));
            re[i] = mono[i] * w;
        }

        Fft.Forward(re, im);

        var half = n / 2;
        var mags = new float[half];
        var peak = 1e-12;
        for (var i = 0; i < half; i++)
        {
            var mag = Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
            mags[i] = (float)mag;
            if (mag > peak) peak = mag;
        }

        for (var i = 0; i < half; i++)
            mags[i] = (float)(Math.Log10(1.0 + mags[i] / peak * 9.0) / Math.Log10(10.0));

        return mags;
    }
}
