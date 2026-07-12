using System;

namespace Ongenet.Core.Models.Audio;

/// <summary>An entry in the arranger playlist, referring to an arrangement marker.</summary>
public sealed class ArrangementSection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MarkerId { get; set; }
}
