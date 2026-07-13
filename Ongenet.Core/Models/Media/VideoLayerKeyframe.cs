using System;

namespace Ongenet.Core.Models.Media;

/// <summary>Beat-synced transform keyframe for a layer item.</summary>
public sealed class VideoLayerKeyframe
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public double Beat { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 0.25;
    public double Height { get; set; } = 0.25;
    public double Opacity { get; set; } = 1;
}
