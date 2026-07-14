using System.IO;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV13Tests
{
    [Fact]
    public void AudioVisualiser_RoundTrip()
    {
        var project = new Project
        {
            Name = "V13 Test",
            VideoEnabled = true
        };
        var group = new Track { Name = "Drums", Kind = TrackKind.Group };
        project.Tracks.Add(group);

        var layer = new VideoLayer
        {
            Name = "Drums Viz",
            AudioSourceTrackId = group.Id,
            WaveformStyle = VideoWaveformStyle.Spectrum,
            WaveformColorArgb = 0xFFFF0000,
            VisualiserColorMode = VideoVisualiserColorMode.Gradient,
            VisualiserColorSecondaryArgb = 0xFF0000FF,
            SpectrumMinHz = 40,
            SpectrumMaxHz = 12000,
            SpectrumLineThickness = 3.5
        };
        project.VideoLayers.Add(layer);

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        Assert.Equal(22, ProjectFile.FormatVersion);
        var viz = loaded.VideoLayers[0];
        Assert.Equal(VideoWaveformStyle.Spectrum, viz.WaveformStyle);
        Assert.Equal(VideoVisualiserColorMode.Gradient, viz.VisualiserColorMode);
        Assert.Equal(0xFF0000FFu, viz.VisualiserColorSecondaryArgb);
        Assert.Equal(40, viz.SpectrumMinHz, 0);
        Assert.Equal(12000, viz.SpectrumMaxHz, 0);
        Assert.Equal(3.5, viz.SpectrumLineThickness, 1);
    }
}
