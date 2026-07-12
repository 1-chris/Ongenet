using System;
using System.Collections.Generic;

namespace Ongenet.Core.Models.Audio;

/// <summary>Step sequencer data for one pattern channel (16/32/64 steps).</summary>
public sealed class StepSequence
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PatternChannelId { get; set; }
    public int StepCount { get; set; } = 16;
    public List<StepData> Steps { get; } = new();
}

/// <summary>Per-step note/velocity/pan/probability.</summary>
public sealed class StepData
{
    public bool Active { get; set; }
    public int Note { get; set; } = 60;
    public float Velocity { get; set; } = 0.8f;
    public float Pan { get; set; }
    public float Probability { get; set; } = 1f;
    public int MicroTimingTicks { get; set; }
}
