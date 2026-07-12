using System;
using System.Collections.Generic;

namespace Ongenet.Core.Models.Audio;

/// <summary>MPE zone and per-note expression routing.</summary>
public sealed class MpeSettings
{
    public bool Enabled { get; set; }
    public int MasterChannel { get; set; } = 1;
    public int MemberChannelStart { get; set; } = 2;
    public int MemberChannelCount { get; set; } = 14;
}

/// <summary>Maps MIDI notes to drum sample lanes.</summary>
public sealed class DrumMap
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Drum Map";
    public List<DrumMapEntry> Entries { get; } = new();
}

public sealed class DrumMapEntry
{
    public int Note { get; set; }
    public string Label { get; set; } = "";
    public Guid? SampleClipId { get; set; }
    public float VelocityScale { get; set; } = 1f;
}

/// <summary>Groove/swing template applied to quantized notes.</summary>
public sealed class GrooveTemplate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Swing 16";
    public double SwingAmount { get; set; } = 0.55;
    public int Division { get; set; } = 16;

    /// <summary>Optional per-step timing offsets (beats), indexed by step index mod Division.</summary>
    public List<double> StepOffsets { get; } = new();
}
