using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class RippleEditServiceTests
{
    [Fact]
    public void InsertTime_ShiftsLaterClips()
    {
        var project = new Project { Name = "Ripple" };
        var track = new Track { Name = "Inst", Kind = TrackKind.Instrument };
        track.Clips.Add(new Clip { Name = "Early", StartBeat = 0, LengthBeats = 4, IsAudio = false });
        track.Clips.Add(new Clip { Name = "Late", StartBeat = 8, LengthBeats = 4, IsAudio = false });
        project.Tracks.Add(track);
        project.PatternClips.Add(new PatternClip { StartBeat = 16, LengthBeats = 4 });

        RippleEditService.InsertTime(project, atBeat: 8, amountBeats: 2);

        Assert.Equal(0, track.Clips[0].StartBeat);
        Assert.Equal(10, track.Clips[1].StartBeat);
        Assert.Equal(18, project.PatternClips[0].StartBeat);
    }
}
