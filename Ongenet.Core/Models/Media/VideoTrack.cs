using System;

namespace Ongenet.Core.Models.Media;

/// <summary>Video track sync metadata (Phase 7).</summary>
public sealed class VideoTrack
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FilePath { get; set; } = "";
    public double OffsetSeconds { get; set; }
    public double Fps { get; set; } = 24;
    public bool Muted { get; set; }

    /// <summary>In-point trim within the source file (seconds).</summary>
    public double InPointSeconds { get; set; }

    /// <summary>Out-point trim within the source file (seconds); 0 = use full duration.</summary>
    public double OutPointSeconds { get; set; }
}
