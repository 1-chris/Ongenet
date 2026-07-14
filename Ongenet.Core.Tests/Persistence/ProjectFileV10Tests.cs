using System.IO;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV10Tests
{
    [Fact]
    public void MultiItemLayersAndRegions_RoundTrip()
    {
        var project = new Project
        {
            Name = "V10 Test",
            VideoEnabled = true,
            VideoCanvasWidth = 1920,
            VideoCanvasHeight = 1080
        };
        var layer = new VideoLayer { Name = "Collage", ZOrder = 0 };
        layer.Items.Add(new VideoLayerItem { Kind = VideoElementKind.Image, SourcePath = "/tmp/a.png", X = 0.1, Y = 0.1 });
        layer.Items.Add(new VideoLayerItem { Kind = VideoElementKind.Image, SourcePath = "/tmp/b.png", X = 0.5, Y = 0.2 });
        project.VideoLayers.Add(layer);
        project.VideoVisibilityRegions.Add(new VideoVisibilityRegion
        {
            LayerId = layer.Id,
            StartBeat = 0,
            EndBeat = 8
        });

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        Assert.Equal(22, ProjectFile.FormatVersion);
        Assert.Single(loaded.VideoLayers);
        Assert.Equal(2, loaded.VideoLayers[0].Items.Count);
        Assert.Single(loaded.VideoVisibilityRegions);
        Assert.Equal(8, loaded.VideoVisibilityRegions[0].EndBeat);
    }

    [Fact]
    public void LegacyLayerItemCreation()
    {
        var layer = new VideoLayer { Name = "Legacy" };
        layer.Items.Add(new VideoLayerItem { SourcePath = "x.png" });
        Assert.Single(layer.Items);
        Assert.Equal("x.png", layer.Items[0].SourcePath);
    }
}
