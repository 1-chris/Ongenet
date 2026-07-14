using System;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>Schedule-time inputs for modulator evaluation.</summary>
public readonly struct ModulatorContext
{
    public Track Track { get; init; }
    public double TimeSec { get; init; }
    public double Beat { get; init; }
    public double Bpm { get; init; }
    public Project? Project { get; init; }
    public Guid SlotId { get; init; }
}
