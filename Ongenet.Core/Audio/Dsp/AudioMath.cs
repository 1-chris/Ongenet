using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>Small shared audio maths: decibel ↔ linear conversions, clamping, soft clip.</summary>
public static class AudioMath
{
    /// <summary>Decibels → linear amplitude (0 dB = 1).</summary>
    public static double Db2Lin(double db) => Math.Pow(10.0, db / 20.0);

    /// <summary>Linear amplitude → decibels (floored to avoid -inf).</summary>
    public static double Lin2Db(double lin) => 20.0 * Math.Log10(Math.Max(1e-9, lin));

    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

    /// <summary>tanh soft clipper.</summary>
    public static float SoftClip(float x) => (float)Math.Tanh(x);

    /// <summary>Linear interpolation between <paramref name="a"/> and <paramref name="b"/> by <paramref name="t"/>.</summary>
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Linear interpolation between <paramref name="a"/> and <paramref name="b"/> by <paramref name="t"/>.</summary>
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>
    /// Equal-power (constant-loudness) pan gains for <paramref name="pan"/> in [-1, 1] (−1 = hard left,
    /// 0 = centre, +1 = hard right). Outputs each channel's gain in [0, 1] with left²+right² == 1.
    /// </summary>
    public static void PanGains(double pan, out float left, out float right)
    {
        pan = Clamp(pan, -1.0, 1.0);
        var angle = (pan + 1.0) * (Math.PI / 4.0); // map −1..1 → 0..π/2
        left = (float)Math.Cos(angle);
        right = (float)Math.Sin(angle);
    }
}
