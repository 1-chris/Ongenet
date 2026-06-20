using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Automation;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>
/// A reusable, time-variant modulation source: a list of breakpoints (x = normalised phase 0..1,
/// y = value 0..1) evaluated with the same per-segment tension shaping as <see cref="AutomationLane"/>,
/// so a curve drawn in the editor reads back identically here. Adds the playback options found on
/// performance "curve" engines: <see cref="Palindrome"/> (forward-then-back), and
/// <see cref="QuantizeSteps"/> (snap the phase to a rhythmic grid for stepped modulation).
///
/// Independent of any one effect — any future effect or plugin can host one to draw automatable shapes.
/// </summary>
public sealed class ModulationCurve
{
    /// <summary>Breakpoints, kept sorted by <see cref="AutomationPoint.Beat"/> (used here as phase 0..1).</summary>
    public List<AutomationPoint> Points { get; } = new();

    /// <summary>When true the phase plays forward then backward over one cycle (smooth, seamless loop).</summary>
    public bool Palindrome { get; set; }

    /// <summary>0 = smooth; &gt;0 snaps the phase to this many equal steps for staircased modulation.</summary>
    public int QuantizeSteps { get; set; }

    public ModulationCurve() { }

    public ModulationCurve(IEnumerable<AutomationPoint> points) => Points.AddRange(points);

    public void Sort() => Points.Sort((a, b) => a.Beat.CompareTo(b.Beat));

    public void Set(IEnumerable<AutomationPoint> points)
    {
        Points.Clear();
        Points.AddRange(points);
        Sort();
    }

    /// <summary>
    /// The curve value at <paramref name="phase"/> (clamped/wrapped to 0..1), after applying palindrome
    /// folding and phase quantisation. Returns 1.0 when the curve is empty (a pass-through gate).
    /// </summary>
    public double Evaluate(double phase)
    {
        if (Points.Count == 0) return 1.0;

        phase = phase < 0 ? 0 : phase > 1 ? 1 : phase;
        if (Palindrome) phase = phase <= 0.5 ? phase * 2.0 : 2.0 * (1.0 - phase); // 0→0, ½→1, 1→0
        if (QuantizeSteps > 0)
        {
            var q = Math.Floor(phase * QuantizeSteps) / QuantizeSteps;
            phase = q > 1 ? 1 : q;
        }

        var pts = Points;
        if (phase <= pts[0].Beat) return pts[0].Value;
        var last = pts[pts.Count - 1];
        if (phase >= last.Beat) return last.Value;

        for (var i = 0; i < pts.Count - 1; i++)
        {
            var p0 = pts[i];
            var p1 = pts[i + 1];
            if (phase < p1.Beat)
            {
                var span = p1.Beat - p0.Beat;
                var f = span <= 0 ? 0 : (phase - p0.Beat) / span;
                return p0.Value + (p1.Value - p0.Value) * AutomationLane.Shape(f, p0.Curve);
            }
        }

        return last.Value;
    }

    public ModulationCurve Clone()
    {
        var c = new ModulationCurve { Palindrome = Palindrome, QuantizeSteps = QuantizeSteps };
        foreach (var p in Points) c.Points.Add(new AutomationPoint(p.Beat, p.Value, p.Curve));
        return c;
    }
}
