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
    public VideoBlendMode BlendMode { get; set; } = VideoBlendMode.Normal;

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

    // 3D Scope (VideoWaveformStyle.Scope3D)
    public double Scope3DCameraYaw { get; set; } = 0.5;
    public double Scope3DCameraPitch { get; set; } = 0.32;
    public double Scope3DCameraDistance { get; set; } = 3.8;
    public double Scope3DLineThickness { get; set; } = 0.018;
    public int Scope3DTrailCount { get; set; } = 20;
    public bool Scope3DTransparentBackground { get; set; } = true;

    // 3D FX layer (VideoLayerContentKind.Engine3D)
    public VideoEngine3DEffectKind? Engine3DEffectKind { get; set; }
    public Guid? Engine3DAudioSourceTrackId { get; set; }
    public string? Engine3DImagePath { get; set; }
    public double Engine3DX { get; set; } = 0.25;
    public double Engine3DY { get; set; } = 0.25;
    public double Engine3DWidth { get; set; } = 0.5;
    public double Engine3DHeight { get; set; } = 0.5;
    public double Engine3DCameraYaw { get; set; } = 0.6;
    public double Engine3DCameraPitch { get; set; } = 0.35;
    public double Engine3DCameraDistance { get; set; } = 4.0;
    public int Engine3DParticleCount { get; set; } = 128;
    public double Engine3DParticleSize { get; set; } = 0.08;
    public uint Engine3DParticleColorArgb { get; set; } = 0xFFBB9AF7;
    public VideoEngine3DParticleShape Engine3DParticleShape { get; set; } = VideoEngine3DParticleShape.Disc;
    public bool Engine3DTransparentBackground { get; set; } = true;

    public bool HasVideoItem => Items.Any(i => i.Kind == VideoElementKind.Video);
    public bool IsWaveformLayer => Items.Count == 0 && AudioSourceTrackId is not null && Engine3DEffectKind is null;
    public bool IsEngine3DLayer => Items.Count == 0 && Engine3DEffectKind is not null;

    public VideoLayerContentKind ContentKind => IsEngine3DLayer
        ? VideoLayerContentKind.Engine3D
        : IsWaveformLayer
            ? VideoLayerContentKind.Waveform
            : Items.Count > 0 ? VideoLayerContentKind.Media : VideoLayerContentKind.Empty;

    public static VideoLayerItem CreateDefaultItem() => new();
}
