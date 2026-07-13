using System;
using System.Collections.Generic;
using System.Linq;

namespace Ongenet.Core.Models.Media;

/// <summary>Composited video layer: collage items plus optional transport-synced video.</summary>
public sealed class VideoLayer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer";
    public int ZOrder { get; set; }
    public double Opacity { get; set; } = 1;
    public bool DefaultVisible { get; set; } = true;

    /// <summary>Collage items composited within this layer.</summary>
    public List<VideoLayerItem> Items { get; } = new();

    /// <summary>Transport sync offset (seconds) for video in this layer.</summary>
    public double OffsetSeconds { get; set; }
    public double InPointSeconds { get; set; }
    public double OutPointSeconds { get; set; }
    public double Fps { get; set; } = 24;
    public Guid? SyncClipId { get; set; }
    public bool Muted { get; set; }

    /// <summary>Audio visualiser: source track or bus (layer-level).</summary>
    public Guid? AudioSourceTrackId { get; set; }
    public VideoWaveformStyle WaveformStyle { get; set; } = VideoWaveformStyle.Mirrored;
    public bool WaveformFollowPlayhead { get; set; } = true;
    public uint WaveformColorArgb { get; set; } = 0xFF179299;
    public VideoVisualiserColorMode VisualiserColorMode { get; set; } = VideoVisualiserColorMode.Solid;
    public uint VisualiserColorSecondaryArgb { get; set; } = 0xFF89B4FA;
    public double SpectrumMinHz { get; set; } = 20;
    public double SpectrumMaxHz { get; set; } = 16000;
    public double SpectrumLineThickness { get; set; } = 2;
    public double WaveformX { get; set; } = 0.1;
    public double WaveformY { get; set; } = 0.7;
    public double WaveformWidth { get; set; } = 0.8;
    public double WaveformHeight { get; set; } = 0.12;

    public bool HasVideoItem => Items.Any(i => i.Kind == VideoElementKind.Video);
    public bool IsWaveformLayer => Items.Count == 0 && AudioSourceTrackId is not null;

    public VideoLayerContentKind ContentKind => IsWaveformLayer
        ? VideoLayerContentKind.Waveform
        : Items.Count > 0 ? VideoLayerContentKind.Media : VideoLayerContentKind.Empty;

    public static VideoLayerItem CreateDefaultItem() => new();
}
