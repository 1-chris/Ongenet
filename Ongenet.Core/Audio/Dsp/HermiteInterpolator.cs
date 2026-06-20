namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// 4-point, 3rd-order Hermite (Catmull-Rom) interpolation for fractional sample reads. Compared to
/// linear interpolation it greatly reduces the high-frequency aliasing heard when a sample is pitched
/// up, at the cost of needing the two neighbours on each side. Reusable by any resampling voice.
/// </summary>
public static class HermiteInterpolator
{
    /// <summary>
    /// Interpolates at fractional position <paramref name="t"/> in [0,1) between <paramref name="y0"/>
    /// and <paramref name="y1"/>, using the outer points <paramref name="ym1"/> (before) and
    /// <paramref name="y2"/> (after) to shape the curve.
    /// </summary>
    public static float Sample(float ym1, float y0, float y1, float y2, float t)
    {
        var c0 = y0;
        var c1 = 0.5f * (y1 - ym1);
        var c2 = ym1 - 2.5f * y0 + 2f * y1 - 0.5f * y2;
        var c3 = 0.5f * (y2 - ym1) + 1.5f * (y0 - y1);
        return ((c3 * t + c2) * t + c1) * t + c0;
    }
}
