using System;

namespace Ongenet.Core.Models.Media;

/// <summary>Beat range during which a video layer is visible on the timeline.</summary>
public sealed class VideoVisibilityRegion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LayerId { get; set; }
    public double StartBeat { get; set; }
    public double EndBeat { get; set; }
    public double FadeInBeats { get; set; }
    public double FadeOutBeats { get; set; }
}
