using System;

namespace Ongenet.Core.Audio.Containers;

/// <summary>Bilinear corner weights for XY morph devices (four-corner crossfade).</summary>
public static class XyMorphMath
{
    /// <summary>
    /// Corner order: bottom-left, bottom-right, top-left, top-right.
    /// <paramref name="x"/> and <paramref name="y"/> are 0..1.
    /// </summary>
    public static void CornerWeights(double x, double y, Span<float> weights)
    {
        if (weights.Length < 4) throw new ArgumentException("Need 4 weight slots.", nameof(weights));
        var xf = (float)Math.Clamp(x, 0, 1);
        var yf = (float)Math.Clamp(y, 0, 1);
        weights[0] = (1f - xf) * (1f - yf);
        weights[1] = xf * (1f - yf);
        weights[2] = (1f - xf) * yf;
        weights[3] = xf * yf;
    }
}
