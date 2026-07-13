using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV7Tests
{
    [Fact]
    public void FormatVersion_IsSeven()
    {
        Assert.Equal(13, ProjectFile.FormatVersion);
    }

    [Fact]
    public void V6AraFieldsStillRoundTrip()
    {
        var project = new Project { Name = "V7 compat" };
        var track = new Track { Name = "Vox", Kind = TrackKind.Audio };
        var clip = new Clip
        {
            Name = "Take",
            IsAudio = true,
            LengthBeats = 4,
            AraPitchOffsetSemitones = -2.5
        };
        track.Clips.Add(clip);
        project.Tracks.Add(track);

        using var ms = new System.IO.MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 16, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        var loadedClip = loaded.Tracks.Single().Clips.Single();
        Assert.Equal(-2.5, loadedClip.AraPitchOffsetSemitones, 3);
    }
}
