using System;
using System.IO;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV15Tests
{
    [Fact]
    public void VideoExportFps_RoundTrip()
    {
        var project = new Project { Name = "V15 Test", VideoEnabled = true, VideoExportFps = 60 };
        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;
        Assert.Equal(21, ProjectFile.FormatVersion);
        Assert.Equal(60, loaded.VideoExportFps, 0);
    }
}

public sealed class ProjectFileV16Tests
{
    [Fact]
    public void VisibilityRegionFades_and_BlendMode_RoundTrip()
    {
        var project = new Project { Name = "V16 Test", VideoEnabled = true };
        var layer = new VideoLayer { Name = "L1", BlendMode = VideoBlendMode.Screen };
        project.VideoLayers.Add(layer);
        project.VideoVisibilityRegions.Add(new VideoVisibilityRegion
        {
            LayerId = layer.Id,
            StartBeat = 0,
            EndBeat = 8,
            FadeInBeats = 1,
            FadeOutBeats = 2
        });
        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;
        Assert.Equal(VideoBlendMode.Screen, loaded.VideoLayers[0].BlendMode);
        Assert.Equal(1, loaded.VideoVisibilityRegions[0].FadeInBeats, 0);
        Assert.Equal(2, loaded.VideoVisibilityRegions[0].FadeOutBeats, 0);
    }
}

public sealed class ProjectFileV17Tests
{
    [Fact]
    public void ChromaKey_and_ColorGrade_RoundTrip()
    {
        var project = new Project { Name = "V17 Test", VideoEnabled = true };
        var layer = new VideoLayer { Name = "FX" };
        layer.Items.Add(new VideoLayerItem
        {
            Kind = VideoElementKind.Video,
            ChromaKeyEnabled = true,
            ChromaKeyTolerance = 0.2,
            Brightness = 1.1,
            Contrast = 0.9,
            Saturation = 1.2,
            MaskImagePath = "mask.png"
        });
        project.VideoLayers.Add(layer);
        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var item = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project.VideoLayers[0].Items[0];
        Assert.True(item.ChromaKeyEnabled);
        Assert.Equal(0.2, item.ChromaKeyTolerance, 2);
        Assert.Equal(1.1, item.Brightness, 2);
        Assert.Equal("mask.png", item.MaskImagePath);
    }
}

public sealed class ProjectFileV18Tests
{
    [Fact]
    public void Keyframes_RoundTrip()
    {
        var project = new Project { Name = "V18 Test", VideoEnabled = true };
        var itemId = Guid.NewGuid();
        project.VideoLayerKeyframes.Add(new VideoLayerKeyframe
        {
            ItemId = itemId,
            Beat = 4,
            X = 0.5,
            Y = 0.5,
            Opacity = 0.8
        });
        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var kf = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project.VideoLayerKeyframes[0];
        Assert.Equal(itemId, kf.ItemId);
        Assert.Equal(4, kf.Beat, 0);
        Assert.Equal(0.8, kf.Opacity, 2);
    }
}

public sealed class ProjectFileV19Tests
{
    [Fact]
    public void Engine3D_and_Scope3D_fields_RoundTrip()
    {
        var trackId = Guid.NewGuid();
        var project = new Project { Name = "V19 Test", VideoEnabled = true };
        project.Tracks.Add(new Track { Id = trackId, Name = "Drums" });
        var scopeLayer = new VideoLayer
        {
            Name = "Scope",
            AudioSourceTrackId = trackId,
            WaveformStyle = VideoWaveformStyle.Scope3D,
            Scope3DCameraYaw = 1.1,
            Scope3DTrailCount = 12,
            Scope3DTransparentBackground = false
        };
        var fxLayer = new VideoLayer
        {
            Name = "FX",
            Engine3DEffectKind = VideoEngine3DEffectKind.Particles,
            Engine3DAudioSourceTrackId = trackId,
            Engine3DParticleCount = 192,
            Engine3DParticleColorArgb = 0xFF40A02B,
            Engine3DParticleShape = VideoEngine3DParticleShape.Quad,
            Engine3DTransparentBackground = true
        };
        project.VideoLayers.Add(scopeLayer);
        project.VideoLayers.Add(fxLayer);

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;
        Assert.Equal(21, ProjectFile.FormatVersion);
        Assert.Equal(VideoWaveformStyle.Scope3D, loaded.VideoLayers[0].WaveformStyle);
        Assert.Equal(12, loaded.VideoLayers[0].Scope3DTrailCount);
        Assert.False(loaded.VideoLayers[0].Scope3DTransparentBackground);
        Assert.Equal(VideoEngine3DEffectKind.Particles, loaded.VideoLayers[1].Engine3DEffectKind);
        Assert.Equal(192, loaded.VideoLayers[1].Engine3DParticleCount);
        Assert.Equal(0xFF40A02Bu, loaded.VideoLayers[1].Engine3DParticleColorArgb);
        Assert.Equal(VideoEngine3DParticleShape.Quad, loaded.VideoLayers[1].Engine3DParticleShape);
        Assert.Equal(trackId, loaded.VideoLayers[1].Engine3DAudioSourceTrackId);
    }
}
