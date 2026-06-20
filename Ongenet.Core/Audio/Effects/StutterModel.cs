using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Modulation;

namespace Ongenet.Core.Audio.Effects;

/// <summary>A named stutter subdivision, measured in quarter-note beats (1/4 note = 1 beat).</summary>
public readonly record struct StutterRate(string Name, double Beats);

/// <summary>How the buffer engine chooses which captured segment each stutter slice repeats.</summary>
public enum BufferMode
{
    /// <summary>Repeat a fixed segment captured when the gesture began (a frozen loop).</summary>
    Lock,

    /// <summary>The segment slides forward with the input so the stutter tracks the source.</summary>
    Slide,

    /// <summary>Grab a random segment from the captured buffer on each repeat (glitch scatter).</summary>
    Random
}

/// <summary>Which quantity a <see cref="ModulationCurve"/> drives. Module depths are keyed separately by id.</summary>
public enum GestureCurveTarget
{
    /// <summary>Per-slice amplitude shape — the headline "stutter shape" drawn by the user.</summary>
    Gate,

    /// <summary>Stutter subdivision swept across the gesture (0 → RateMin, 1 → RateMax).</summary>
    Rate,

    /// <summary>Built-in low-pass cutoff swept across the gesture.</summary>
    Cutoff
}

/// <summary>
/// One performable "gesture": the stutter/buffer/gate settings plus the time-variant curves that shape
/// them, all triggered as a unit by a key (MIDI mode) or by the transport (Auto mode). Mirrors a single
/// Stutter Edit-style mapping. Curves live in two phase domains: the <see cref="Gate"/> curve is sampled
/// across each individual slice (0..1), while <see cref="Rate"/>/<see cref="Cutoff"/>/<see cref="ModuleCurves"/>
/// are sampled across the whole gesture (0..1 over <see cref="LengthBeats"/>).
/// </summary>
public sealed class StutterGesture
{
    public string Name { get; set; } = "Gesture";

    /// <summary>Length of one full curve cycle, in beats.</summary>
    public double LengthBeats { get; set; } = 4.0;

    /// <summary>Default subdivision (index into <see cref="Rates"/>) when no Rate curve is assigned.</summary>
    public int RateIndex { get; set; } = 2; // 1/16

    /// <summary>Rate-curve endpoints: value 0 → this index, value 1 → <see cref="RateMaxIndex"/>.</summary>
    public int RateMinIndex { get; set; } = 1; // 1/8

    public int RateMaxIndex { get; set; } = 6; // 1/256

    public BufferMode Buffer { get; set; } = BufferMode.Lock;

    /// <summary>Captured segment length the buffer engine repeats, in beats.</summary>
    public double BufferLengthBeats { get; set; } = 0.25;

    /// <summary>Per-slice release (tail) in milliseconds — 0 = hard click, higher = smooth fade.</summary>
    public double TailMs { get; set; } = 15.0;

    /// <summary>Per-slice amplitude shape (the "stutter shape"). Empty = full, square gate.</summary>
    public ModulationCurve Gate { get; set; } = new(new[] { new Automation.AutomationPoint(0, 1) });

    /// <summary>Stutter-rate sweep across the gesture; null = fixed <see cref="RateIndex"/>.</summary>
    public ModulationCurve? Rate { get; set; }

    /// <summary>Low-pass cutoff sweep across the gesture; null = built-in filter off.</summary>
    public ModulationCurve? Cutoff { get; set; }

    /// <summary>Optional per-module depth curves, keyed by <see cref="Modules.FxModule.Id"/>.</summary>
    public Dictionary<string, ModulationCurve> ModuleCurves { get; } = new();

    public StutterGesture Clone()
    {
        var g = new StutterGesture
        {
            Name = Name,
            LengthBeats = LengthBeats,
            RateIndex = RateIndex,
            RateMinIndex = RateMinIndex,
            RateMaxIndex = RateMaxIndex,
            Buffer = Buffer,
            BufferLengthBeats = BufferLengthBeats,
            TailMs = TailMs,
            Gate = Gate.Clone(),
            Rate = Rate?.Clone(),
            Cutoff = Cutoff?.Clone()
        };
        foreach (var (k, v) in ModuleCurves) g.ModuleCurves[k] = v.Clone();
        return g;
    }
}

/// <summary>Shared constants/helpers for the stutter engine.</summary>
public static class StutterRates
{
    /// <summary>Subdivisions from 1/4 note down to 1/512, index-aligned with gesture rate indices.</summary>
    public static readonly StutterRate[] All =
    {
        new("1/4", 1.0),
        new("1/8", 0.5),
        new("1/16", 0.25),
        new("1/32", 0.125),
        new("1/64", 0.0625),
        new("1/128", 0.03125),
        new("1/256", 0.015625),
        new("1/512", 0.0078125),
    };

    public static double BeatsFor(int index) => All[Math.Clamp(index, 0, All.Length - 1)].Beats;
}
