using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Automation;

/// <summary>
/// A line-graph automation curve that drives one <see cref="IAutomationTarget"/> over time. Points are
/// kept sorted by beat; <see cref="Evaluate"/> interpolates between them (with per-segment tension) and
/// clamps to the first/last value outside the range. <see cref="IsArmed"/> marks the lane for recording.
/// </summary>
public sealed class AutomationLane
{
    public AutomationLane(IAutomationTarget target)
    {
        Target = target;
    }

    public IAutomationTarget Target { get; }
    public List<AutomationPoint> Points { get; } = new();
    public bool IsArmed { get; set; }

    /// <summary>Serializable description of what this lane drives, for project save/load. Null if unknown.</summary>
    public AutomationBinding? Binding { get; set; }

    public string Name => Target.Name;
    public double Minimum => Target.Minimum;
    public double Maximum => Target.Maximum;

    public void AddPoint(AutomationPoint point)
    {
        Points.Add(point);
        Sort();
    }

    public void RemovePoint(AutomationPoint point) => Points.Remove(point);

    public void Sort() => Points.Sort((a, b) => a.Beat.CompareTo(b.Beat));

    /// <summary>The automation value at <paramref name="beat"/> (clamped to the ends).</summary>
    public double Evaluate(double beat)
    {
        var pts = Points;
        if (pts.Count == 0) return Minimum;
        if (beat <= pts[0].Beat) return pts[0].Value;
        var last = pts[pts.Count - 1];
        if (beat >= last.Beat) return last.Value;

        for (var i = 0; i < pts.Count - 1; i++)
        {
            var p0 = pts[i];
            var p1 = pts[i + 1];
            if (beat < p1.Beat)
            {
                var span = p1.Beat - p0.Beat;
                var f = span <= 0 ? 0 : (beat - p0.Beat) / span;
                return p0.Value + (p1.Value - p0.Value) * Shape(f, p0.Curve);
            }
        }

        return last.Value;
    }

    /// <summary>
    /// Maps a 0..1 fraction through a tension curve. 0 = linear; positive eases out (rises early),
    /// negative eases in (rises late). Shared by the engine and the editor so the drawn line matches.
    /// </summary>
    public static double Shape(double f, double curve)
    {
        if (f <= 0) return 0;
        if (f >= 1) return 1;
        if (curve == 0) return f;
        var exponent = Math.Pow(2.0, -curve * 2.5);
        return Math.Pow(f, exponent);
    }
}
