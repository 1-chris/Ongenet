using System.IO;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV11Tests
{
    [Fact]
    public void UnifiedLayers_RoundTrip()
    {
        var project = new Project
        {
            Name = "V11 Test",
            VideoEnabled = true,
            VideoCanvasWidth = 1920,
            VideoCanvasHeight = 1080
        };
        var videoLayer = new VideoLayer { Name = "Background", ZOrder = 0, OffsetSeconds = 1, Fps = 30 };
        videoLayer.Items.Add(new VideoLayerItem
        {
            Kind = VideoElementKind.Video,
            SourcePath = "/tmp/bg.mp4",
            X = 0, Y = 0, Width = 1, Height = 1
        });
        var collage = new VideoLayer { Name = "Logo", ZOrder = 1 };
        collage.Items.Add(new VideoLayerItem { Kind = VideoElementKind.Image, SourcePath = "/tmp/logo.png" });
        project.VideoLayers.Add(videoLayer);
        project.VideoLayers.Add(collage);
        project.VideoVisibilityRegions.Add(new VideoVisibilityRegion
        {
            LayerId = collage.Id,
            StartBeat = 0,
            EndBeat = 8
        });

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        Assert.Equal(22, ProjectFile.FormatVersion);
        Assert.Equal(2, loaded.VideoLayers.Count);
        Assert.True(loaded.VideoLayers[0].HasVideoItem);
        Assert.Single(loaded.VideoVisibilityRegions);
        Assert.Equal(8, loaded.VideoVisibilityRegions[0].EndBeat);
    }
}
