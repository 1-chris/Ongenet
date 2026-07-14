using System.IO;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV9Tests
{
    [Fact]
    public void VideoCanvasDimensions_RoundTrip()
    {
        var project = new Project
        {
            Name = "V9 Canvas",
            VideoEnabled = true,
            VideoCanvasWidth = 1280,
            VideoCanvasHeight = 720
        };
        var layer = new VideoLayer { Name = "Logo" };
        layer.Items.Add(new VideoLayerItem { Kind = VideoElementKind.Image });
        project.VideoLayers.Add(layer);

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        Assert.Equal(23, ProjectFile.FormatVersion);
        Assert.Equal(1280, loaded.VideoCanvasWidth);
        Assert.Equal(720, loaded.VideoCanvasHeight);
        Assert.True(loaded.VideoEnabled);
        Assert.Single(loaded.VideoLayers);
    }
}
