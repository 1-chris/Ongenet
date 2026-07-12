using System;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>Surround panning coefficients for 5.1 / 7.1 export (Phase 7).</summary>
public static class SurroundPanner
{
    public static (float L, float R, float C, float Lfe, float Ls, float Rs) Pan51(double pan, double width = 1.0,
        SurroundChannelPan? custom = null)
    {
        if (custom is not null)
        {
            return ((float)custom.FrontLeft, (float)custom.FrontRight, (float)custom.Center, (float)custom.Lfe,
                (float)custom.SurroundLeft, (float)custom.SurroundRight);
        }

        var p = Math.Clamp(pan, -1.0, 1.0);
        var w = (float)Math.Clamp(width, 0, 1);
        var l = (float)((1 - p) * 0.5);
        var r = (float)((1 + p) * 0.5);
        return (l * w, r * w, 0.707f * w, 0f, l * (1 - w), r * (1 - w));
    }

    /// <summary>7.1 surround coefficients (L, R, C, LFE, Ls, Rs, Sl, Sr).</summary>
    public static (float L, float R, float C, float Lfe, float Ls, float Rs, float Sl, float Sr) Pan71(
        double pan, double width = 1.0, SurroundChannelPan? custom = null)
    {
        if (custom is not null)
        {
            return ((float)custom.FrontLeft, (float)custom.FrontRight, (float)custom.Center, (float)custom.Lfe,
                (float)custom.SurroundLeft, (float)custom.SurroundRight, (float)custom.RearLeft,
                (float)custom.RearRight);
        }

        var (l, r, c, lfe, ls, rs) = Pan51(pan, width);
        var rear = (1 - (float)Math.Clamp(width, 0, 1)) * 0.5f;
        return (l, r, c, lfe, ls * 0.85f, rs * 0.85f, l * rear, r * rear);
    }
}
