using System;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Normalised biquad coefficients (a0 = 1) for the RBJ "Audio EQ Cookbook" filter shapes. Used by
/// <see cref="FilterEffect"/> for processing and by the UI's analyser graph to plot the response.
/// </summary>
public readonly struct BiquadCoefficients
{
    public readonly double B0, B1, B2, A1, A2;

    public BiquadCoefficients(double b0, double b1, double b2, double a1, double a2)
    {
        B0 = b0; B1 = b1; B2 = b2; A1 = a1; A2 = a2;
    }

    /// <summary>An identity (pass-through) filter.</summary>
    public static BiquadCoefficients Identity => new(1, 0, 0, 0, 0);

    /// <summary>
    /// Computes coefficients for <paramref name="mode"/> at the given cutoff/centre frequency and Q.
    /// Frequency is clamped to a stable range below Nyquist. Bypass returns <see cref="Identity"/>.
    /// </summary>
    public static BiquadCoefficients Compute(FilterMode mode, double freq, double q, double sampleRate)
    {
        if (mode == FilterMode.Bypass || sampleRate <= 0) return Identity;

        var nyquist = sampleRate * 0.5;
        freq = Math.Clamp(freq, 10.0, nyquist * 0.99);
        q = Math.Max(0.05, q);

        var w0 = 2.0 * Math.PI * freq / sampleRate;
        var cos = Math.Cos(w0);
        var sin = Math.Sin(w0);
        var alpha = sin / (2.0 * q);

        double b0, b1, b2, a0, a1, a2;
        switch (mode)
        {
            case FilterMode.LowPass:
                b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = (1 - cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case FilterMode.HighPass:
                b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = (1 + cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case FilterMode.BandPass: // constant 0 dB peak gain
                b0 = alpha; b1 = 0; b2 = -alpha;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case FilterMode.Notch:
                b0 = 1; b1 = -2 * cos; b2 = 1;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            default:
                return Identity;
        }

        return new BiquadCoefficients(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>
    /// Computes coefficients for an EQ band: peaking (Bell) and shelves use <paramref name="gainDb"/>;
    /// HighPass/LowPass/Notch reuse <see cref="Compute"/> (gain ignored).
    /// </summary>
    public static BiquadCoefficients ComputeEq(EqBandType type, double freq, double q, double gainDb, double sampleRate)
    {
        switch (type)
        {
            case EqBandType.HighPass: return Compute(FilterMode.HighPass, freq, q, sampleRate);
            case EqBandType.LowPass: return Compute(FilterMode.LowPass, freq, q, sampleRate);
            case EqBandType.Notch: return Compute(FilterMode.Notch, freq, q, sampleRate);
        }

        if (sampleRate <= 0) return Identity;
        var nyquist = sampleRate * 0.5;
        freq = Math.Clamp(freq, 10.0, nyquist * 0.99);
        q = Math.Max(0.05, q);

        var a = Math.Pow(10.0, gainDb / 40.0);
        var w0 = 2.0 * Math.PI * freq / sampleRate;
        var cos = Math.Cos(w0);
        var sin = Math.Sin(w0);
        var alpha = sin / (2.0 * q);

        double b0, b1, b2, a0, a1, a2;
        switch (type)
        {
            case EqBandType.Bell:
                b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a;
                a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a;
                break;
            case EqBandType.LowShelf:
            {
                var tsa = 2 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) - (a - 1) * cos + tsa);
                b1 = 2 * a * ((a - 1) - (a + 1) * cos);
                b2 = a * ((a + 1) - (a - 1) * cos - tsa);
                a0 = (a + 1) + (a - 1) * cos + tsa;
                a1 = -2 * ((a - 1) + (a + 1) * cos);
                a2 = (a + 1) + (a - 1) * cos - tsa;
                break;
            }
            case EqBandType.HighShelf:
            {
                var tsa = 2 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) + (a - 1) * cos + tsa);
                b1 = -2 * a * ((a - 1) + (a + 1) * cos);
                b2 = a * ((a + 1) + (a - 1) * cos - tsa);
                a0 = (a + 1) - (a - 1) * cos + tsa;
                a1 = 2 * ((a - 1) - (a + 1) * cos);
                a2 = (a + 1) - (a - 1) * cos - tsa;
                break;
            }
            default:
                return Identity;
        }

        return new BiquadCoefficients(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>The filter's magnitude response at <paramref name="freqHz"/>, in decibels.</summary>
    public double MagnitudeDb(double freqHz, double sampleRate)
    {
        var w = 2.0 * Math.PI * freqHz / sampleRate;
        double c1 = Math.Cos(w), s1 = Math.Sin(w);
        double c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);

        var numRe = B0 + B1 * c1 + B2 * c2;
        var numIm = -(B1 * s1 + B2 * s2);
        var denRe = 1 + A1 * c1 + A2 * c2;
        var denIm = -(A1 * s1 + A2 * s2);

        var numMag2 = numRe * numRe + numIm * numIm;
        var denMag2 = denRe * denRe + denIm * denIm;
        if (denMag2 <= 1e-20) return -120.0;

        var db = 10.0 * Math.Log10(numMag2 / denMag2);
        return Math.Clamp(db, -120.0, 60.0);
    }
}
