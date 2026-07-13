using System;
using System.Collections.Generic;

namespace Ongenet.Core.Models.Audio;

/// <summary>Comping take lane under a parent track.</summary>
public sealed class TakeLane
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Take";
    public List<Take> Takes { get; } = new();
    public bool IsExpanded { get; set; } = true;
    /// <summary>When true, this lane is preferred for incoming comp recordings.</summary>
    public bool IsArmedForRecord { get; set; }

    /// <summary>Custom timeline row height in pixels; 0 = the default (36px).</summary>
    public double LaneHeight { get; set; }
}

public sealed class Take
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ClipId { get; set; }
    public bool IsSelected { get; set; }
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; }
}
