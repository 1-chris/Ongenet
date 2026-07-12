using System;

namespace Ongenet.Core.Models.Audio;

/// <summary>Named cue point on the arrangement timeline.</summary>
public sealed class ArrangementMarker
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Marker";
    public double Beat { get; set; }
}
