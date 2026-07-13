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

    /// <summary>Text overlay content when <see cref="Kind"/> is <see cref="VideoElementKind.Text"/>.</summary>
    public string TextContent { get; set; } = "";

    public double FontSizePx { get; set; } = 48;
    public uint TextColorArgb { get; set; } = 0xFFFFFFFF;

    /// <summary>Subtitle clip from arrangement when <see cref="Kind"/> is <see cref="VideoElementKind.Subtitle"/>.</summary>
    public Guid? SubtitleClipId { get; set; }

    /// <summary>External SRT file for subtitles.</summary>
    public string? SubtitleSrtPath { get; set; }

    /// <summary>Alpha mask image path applied to this item.</summary>
    public string? MaskImagePath { get; set; }

    public bool ChromaKeyEnabled { get; set; }
    public uint ChromaKeyColorArgb { get; set; } = 0xFF00FF00;
    public double ChromaKeyTolerance { get; set; } = 0.15;
    public double ChromaKeyFeather { get; set; } = 0.05;

    public double Brightness { get; set; } = 1;
    public double Contrast { get; set; } = 1;
    public double Saturation { get; set; } = 1;
    public string? LutCubePath { get; set; }
}
