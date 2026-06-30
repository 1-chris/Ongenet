using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A small in-place radix-2 complex FFT (power-of-two lengths only). Reusable for any spectral work —
/// here it builds band-limited mip levels for <see cref="Wavetable"/>. Not allocation-free, so use it
/// off the audio thread (e.g. when a table is built), not inside Process.
/// </summary>
public static class Fft
{
    public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>Forward transform: <c>X[k] = Σ x[n]·e^(-i2πkn/N)</c> (unnormalised), in place.</summary>
    public static void Forward(double[] re, double[] im) => Transform(re, im, -1);

    /// <summary>Inverse transform (normalised by 1/N), in place.</summary>
    public static void Inverse(double[] re, double[] im)
    {
        Transform(re, im, +1);
        var n = re.Length;
        var s = 1.0 / n;
        for (var i = 0; i < n; i++) { re[i] *= s; im[i] *= s; }
    }

    private static void Transform(double[] re, double[] im, int sign)
    {
        var n = re.Length;
        if (!IsPowerOfTwo(n)) throw new ArgumentException("FFT length must be a power of two.");

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = sign * 2.0 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    var tr = re[b] * cr - im[b] * ci;
                    var ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    var ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }
    }
}
