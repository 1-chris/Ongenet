using System;

namespace Ongenet.Core.Models.Audio;

/// <summary>An auxiliary send from a track to a return bus.</summary>
public sealed class TrackSend
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The return track id (typically <see cref="TrackKind.Return"/>).</summary>
    public Guid TargetTrackId { get; set; }

    /// <summary>Send level, linear 0..1.</summary>
    public double Level { get; set; } = 0.5;

    /// <summary>When true, taps the signal before the track fader; otherwise post-fader.</summary>
    public bool PreFader { get; set; }

    public bool Enabled { get; set; } = true;
}
