using System.IO;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV14Tests
{
    [Fact]
    public void TextLayerItem_RoundTrip()
    {
        var project = new Project { Name = "V14 Test", VideoEnabled = true };
        var layer = new VideoLayer { Name = "Titles" };
        layer.Items.Add(new VideoLayerItem
        {
            Kind = VideoElementKind.Text,
            TextContent = "Hello Ongenet",
            FontSizePx = 64,
            TextColorArgb = 0xFFFFEE00,
            X = 0.2,
            Y = 0.1,
            Width = 0.6,
            Height = 0.2
        });
        project.VideoLayers.Add(layer);

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        Assert.Equal(22, ProjectFile.FormatVersion);
        var item = loaded.VideoLayers[0].Items[0];
        Assert.Equal(VideoElementKind.Text, item.Kind);
        Assert.Equal("Hello Ongenet", item.TextContent);
        Assert.Equal(64, item.FontSizePx, 0);
        Assert.Equal(0xFFFFEE00u, item.TextColorArgb);
    }
}
