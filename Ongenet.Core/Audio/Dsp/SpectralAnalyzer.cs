using System;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>Offline FFT magnitude analysis for waveform spectral preview.</summary>
public static class SpectralAnalyzer
{
    /// <summary>
    /// Computes normalised magnitude bins (0..1) from the first <paramref name="fftSize"/> samples
    /// of a mono mix of <paramref name="buffer"/>. <paramref name="fftSize"/> must be a power of two.
    /// </summary>
    public static float[] ComputeMagnitudes(AudioSampleBuffer buffer, int fftSize = 2048)
    {
        if (!Fft.IsPowerOfTwo(fftSize)) fftSize = 2048;
        if (buffer.FrameCount <= 0 || buffer.SampleRate <= 0) return Array.Empty<float>();

        var mono = MixToMono(buffer);
        var n = Math.Min(fftSize, mono.Length);
        if (n < 64) return Array.Empty<float>();

        var re = new double[n];
        var im = new double[n];
        for (var i = 0; i < n; i++)
        {
            // Hann window reduces spectral leakage on finite clips.
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

    private static float[] MixToMono(AudioSampleBuffer buffer)
    {
        var ch = buffer.Channels < 1 ? 1 : buffer.Channels;
        var frames = buffer.FrameCount;
        var mono = new float[frames];
        var samples = buffer.Samples;
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var c = 0; c < ch; c++)
                sum += samples[f * ch + c];
            mono[f] = (float)(sum / ch);
        }

        return mono;
    }
}
