using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

/// <summary>Round-trips the clip's PitchCorrected flag through save/load and the cloner.</summary>
public class ClipPitchCorrectedPersistenceTests
{
    private static Project BuildProject(bool pitchCorrected)
    {
        var project = new Project();
        var track = new Track { Name = "Audio", Kind = TrackKind.Audio };
        track.Clips.Add(new Clip
        {
            Name = "Loop",
            IsAudio = true,
            StartBeat = 0,
            LengthBeats = 4,
            StretchToTempo = true,
            PitchCorrected = pitchCorrected
        });
        project.Tracks.Add(track);
        return project;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SaveLoadPreservesPitchCorrected(bool pitchCorrected)
    {
        var project = BuildProject(pitchCorrected);

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 0, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        var clip = loaded.Tracks.Single().Clips.Single();
        Assert.Equal(pitchCorrected, clip.PitchCorrected);
        Assert.True(clip.StretchToTempo);
    }

    [Fact]
    public void ClonerPreservesPitchCorrected()
    {
        var clone = ProjectCloner.Clone(BuildProject(true), new InstrumentRegistry(), new EffectRegistry());
        Assert.True(clone.Tracks.Single().Clips.Single().PitchCorrected);
    }
}
