using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Files;

/// <summary>Math helpers ported from Queen Mary qm-dsp <c>MathUtilities</c>.</summary>
internal static class QueenMaryMath
{
    private const double Eps = 0.0000008;

    public static double PrincArg(double ang)
    {
        return Mod(ang + Math.PI, -2.0 * Math.PI) + Math.PI;
    }

    public static int NextPowerOfTwo(int x)
    {
        if (x < 1) return 1;
        if (IsPowerOfTwo(x)) return x;
        var n = 1;
        while (x != 0)
        {
            x >>= 1;
            n <<= 1;
        }

        return n;
    }

    public static void AdaptiveThreshold(List<double> data)
    {
        if (data.Count == 0) return;

        const int pre = 8;
        const int post = 7;
        var smoothed = new double[data.Count];
        for (var i = 0; i < data.Count; i++)
        {
            var first = Math.Max(0, i - pre);
            var last = Math.Min(data.Count - 1, i + post);
            var sum = 0.0;
            for (var j = first; j <= last; j++) sum += data[j];
            smoothed[i] = sum / (last - first + 1);
        }

        for (var i = 0; i < data.Count; i++)
        {
            data[i] -= smoothed[i];
            if (data[i] < 0.0) data[i] = 0.0;
        }
    }

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var scratch = new double[values.Count];
        for (var i = 0; i < values.Count; i++) scratch[i] = values[i];
        Array.Sort(scratch);
        var middle = scratch.Length / 2;
        return scratch.Length % 2 == 0
            ? (scratch[middle] + scratch[middle - 1]) / 2.0
            : scratch[middle];
    }

    public static double FoldToDanceRange(double bpm)
    {
        while (bpm < 70.0) bpm *= 2.0;
        while (bpm > 180.0) bpm /= 2.0;
        return bpm;
    }

    public static double Mean(double[] values, int length)
    {
        if (length <= 0) return 0;
        var sum = 0.0;
        for (var i = 0; i < length; i++) sum += values[i];
        return sum / length;
    }

    public static void NormalizeUnitMax(double[] data)
    {
        var max = 0.0;
        for (var i = 0; i < data.Length; i++)
            max = Math.Max(max, Math.Abs(data[i]));
        if (max <= 0) return;
        for (var i = 0; i < data.Length; i++)
            data[i] /= max;
    }

    public static int GetMaxIndex(double[] values, out double maxVal)
    {
        maxVal = double.NegativeInfinity;
        var idx = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] > maxVal)
            {
                maxVal = values[i];
                idx = i;
            }
        }

        return idx;
    }

    private static double Mod(double x, double y)
    {
        var a = Math.Floor(x / y);
        return x - y * a;
    }

    private static bool IsPowerOfTwo(int x) => x > 0 && (x & (x - 1)) == 0;

    internal static double Epsilon => Eps;
}
