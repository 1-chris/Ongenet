using System.IO;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV12Tests
{
    [Fact]
    public void WaveformLayer_RoundTrip()
    {
        var project = new Project
        {
            Name = "V12 Test",
            VideoEnabled = true,
            VideoCanvasWidth = 1920,
            VideoCanvasHeight = 1080
        };
        var drums = new Track { Name = "Drums", Kind = TrackKind.Group };
        project.Tracks.Add(drums);

        var layer = new VideoLayer
        {
            Name = "Drums WF",
            ZOrder = 0,
            AudioSourceTrackId = drums.Id,
            WaveformStyle = VideoWaveformStyle.Mirrored,
            WaveformFollowPlayhead = true,
            WaveformColorArgb = 0xFFFF0000,
            WaveformX = 0.1,
            WaveformY = 0.7,
            WaveformWidth = 0.8,
            WaveformHeight = 0.12
        };
        project.VideoLayers.Add(layer);

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        Assert.Equal(22, ProjectFile.FormatVersion);
        Assert.Single(loaded.VideoLayers);
        var wf = loaded.VideoLayers[0];
        Assert.True(wf.IsWaveformLayer);
        Assert.Equal(drums.Id, wf.AudioSourceTrackId);
        Assert.Equal(VideoWaveformStyle.Mirrored, wf.WaveformStyle);
        Assert.True(wf.WaveformFollowPlayhead);
        Assert.Equal(0xFFFF0000u, wf.WaveformColorArgb);
        Assert.Equal(0.1, wf.WaveformX, 3);
        Assert.Equal(0.7, wf.WaveformY, 3);
        Assert.Equal(0.8, wf.WaveformWidth, 3);
        Assert.Equal(0.12, wf.WaveformHeight, 3);
        Assert.Empty(wf.Items);
    }
}
