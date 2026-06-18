using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>Window (envelope) shapes for a single grain, indexed by a normalised phase 0..1.</summary>
public enum GrainWindowShape
{
    /// <summary>Raised cosine (Hann) — smooth, the default click-free window.</summary>
    Hann,

    /// <summary>Linear up then down.</summary>
    Triangle,

    /// <summary>Flat top with raised-cosine edges (a softened rectangle); good for sustained textures.</summary>
    Tukey,

    /// <summary>Bell curve — very smooth, narrow effective width.</summary>
    Gaussian,

    /// <summary>Steep attack, long exponential decay — plucky/percussive grains.</summary>
    ExpoDecay,

    /// <summary>No fade (full level) — hard edges, will click; offered for completeness.</summary>
    Rectangular
}

/// <summary>
/// Grain window functions. <see cref="Value"/> maps a grain's normalised position (0 = grain start,
/// 1 = grain end) to an amplitude in [0, 1]. Shared so any granular/overlap-add code can reuse them.
/// </summary>
public static class GrainWindow
{
    private const double TukeyTaper = 0.4; // fraction of each edge that fades (Tukey)

    // Precomputed window tables: evaluating the shapes (cos/exp/pow) per grain per sample is far too
    // expensive in the audio loop, so build them once and look up. Nearest-bin lookup is plenty smooth
    // at this resolution.
    private const int TableSize = 2048;
    private static readonly float[][] Tables = BuildTables();

    private static float[][] BuildTables()
    {
        var count = 0;
        foreach (GrainWindowShape s in Enum.GetValues(typeof(GrainWindowShape)))
            if ((int)s + 1 > count) count = (int)s + 1;

        var tables = new float[count][];
        foreach (GrainWindowShape s in Enum.GetValues(typeof(GrainWindowShape)))
        {
            var t = new float[TableSize];
            for (var i = 0; i < TableSize; i++) t[i] = (float)Value(s, (i + 0.5) / TableSize);
            tables[(int)s] = t;
        }

        return tables;
    }

    /// <summary>Fast table lookup of the window amplitude (audio-thread friendly). 0 at the edges.</summary>
    public static float Lookup(GrainWindowShape shape, double phase)
    {
        if (phase <= 0.0 || phase >= 1.0) return 0f;
        var si = (int)shape;
        if (si < 0 || si >= Tables.Length) si = 0;
        var idx = (int)(phase * TableSize);
        if (idx < 0) idx = 0;
        else if (idx >= TableSize) idx = TableSize - 1;
        return Tables[si][idx];
    }

    public static double Value(GrainWindowShape shape, double phase)
    {
        if (phase <= 0.0 || phase >= 1.0) return 0.0;

        switch (shape)
        {
            case GrainWindowShape.Triangle:
                return 1.0 - Math.Abs(2.0 * phase - 1.0);

            case GrainWindowShape.Tukey:
            {
                if (phase < TukeyTaper)
                    return 0.5 * (1.0 - Math.Cos(Math.PI * phase / TukeyTaper));
                if (phase > 1.0 - TukeyTaper)
                    return 0.5 * (1.0 - Math.Cos(Math.PI * (1.0 - phase) / TukeyTaper));
                return 1.0;
            }

            case GrainWindowShape.Gaussian:
            {
                const double sigma = 0.18;
                var x = (phase - 0.5) / sigma;
                return Math.Exp(-0.5 * x * x);
            }

            case GrainWindowShape.ExpoDecay:
            {
                // Quick raised-cosine attack, exponential decay to the grain end.
                const double attack = 0.05;
                var a = phase < attack ? 0.5 * (1.0 - Math.Cos(Math.PI * phase / attack)) : 1.0;
                return a * Math.Exp(-3.5 * phase);
            }

            case GrainWindowShape.Rectangular:
                return 1.0;

            default: // Hann
                return 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * phase));
        }
    }
}
