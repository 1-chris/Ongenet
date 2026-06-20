using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Automation;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>
/// A library of ready-made <see cref="ModulationCurve"/> shapes (the "curve presets" a performance
/// stutter engine ships with). Each entry is a name plus a factory that produces the breakpoints, so
/// the UI can list them and apply one to any curve. Reusable wherever a <see cref="ModulationCurve"/>
/// is edited.
/// </summary>
public static class CurveShapes
{
    public sealed record Shape(string Name, Func<List<AutomationPoint>> Build);

    private static AutomationPoint P(double phase, double value, double curve = 0) => new(phase, value, curve);

    /// <summary>The 12 built-in shapes, in display order.</summary>
    public static readonly IReadOnlyList<Shape> All = new List<Shape>
    {
        new("Full On", () => new() { P(0, 1) }),
        new("Ramp Up", () => new() { P(0, 0), P(1, 1) }),
        new("Ramp Down", () => new() { P(0, 1), P(1, 0) }),
        new("Exp Up", () => new() { P(0, 0, 0.7), P(1, 1) }),
        new("Exp Down", () => new() { P(0, 1, -0.7), P(1, 0) }),
        new("Triangle", () => new() { P(0, 0), P(0.5, 1), P(1, 0) }),
        new("Sine", BuildSine),
        new("Pulse", () => new() { P(0, 1), P(0.49, 1), P(0.5, 0), P(1, 0) }),
        new("Stairs Up", () => BuildStairs(4, ascending: true)),
        new("Stairs Down", () => BuildStairs(4, ascending: false)),
        new("Gate 1/4", () => BuildGate(4)),
        new("Gate 1/8", () => BuildGate(8)),
    };

    public static void Apply(ModulationCurve curve, int index)
    {
        if (index < 0 || index >= All.Count) return;
        curve.Set(All[index].Build());
    }

    private static List<AutomationPoint> BuildSine()
    {
        var pts = new List<AutomationPoint>();
        const int steps = 8;
        for (var i = 0; i <= steps; i++)
        {
            var phase = (double)i / steps;
            var value = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * phase); // 0→0, ½→1, 1→0
            pts.Add(P(phase, value));
        }

        return pts;
    }

    // A staircase of <paramref name="steps"/> flat treads rising (or falling) across the phase.
    private static List<AutomationPoint> BuildStairs(int steps, bool ascending)
    {
        var pts = new List<AutomationPoint>();
        for (var i = 0; i < steps; i++)
        {
            var level = ascending ? (double)i / (steps - 1) : 1.0 - (double)i / (steps - 1);
            var start = (double)i / steps;
            var end = (i + 1.0) / steps - 1e-4;
            pts.Add(P(start, level));
            pts.Add(P(end, level));
        }

        return pts;
    }

    // A rhythmic on/off gate with <paramref name="divisions"/> equal cells (on for the first half of each).
    private static List<AutomationPoint> BuildGate(int divisions)
    {
        var pts = new List<AutomationPoint>();
        for (var i = 0; i < divisions; i++)
        {
            var start = (double)i / divisions;
            var mid = (i + 0.5) / divisions;
            pts.Add(P(start, 1));
            pts.Add(P(mid - 1e-4, 1));
            pts.Add(P(mid, 0));
            pts.Add(P((i + 1.0) / divisions - 1e-4, 0));
        }

        return pts;
    }
}
