using System;

namespace Ongenet.Core.Models.Media;

/// <summary>One asset within a video layer (image, GIF, video clip, etc.).</summary>
public sealed class VideoLayerItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public VideoElementKind Kind { get; set; } = VideoElementKind.Image;
    public string SourcePath { get; set; } = "";

    /// <summary>Normalized frame bounds (0..1).</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 0.25;
    public double Height { get; set; } = 0.25;
    public double Rotation { get; set; }
    public double Opacity { get; set; } = 1;
}
