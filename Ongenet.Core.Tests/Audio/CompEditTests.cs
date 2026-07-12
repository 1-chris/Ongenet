using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;

namespace Ongenet.Core.Tests.Audio;

public sealed class CompEditTests
{
    private const double Bpm = 120.0;

    [Fact]
    public void PromoteTake_KeepsSelectedClipOnly()
    {
        var track = new Track { Name = "Vox", Kind = TrackKind.Audio };
        var a = new Clip { Name = "A", IsAudio = true, StartBeat = 0, LengthBeats = 4, Samples = Buffer(4) };
        var b = new Clip { Name = "B", IsAudio = true, StartBeat = 0, LengthBeats = 4, Samples = Buffer(4) };
        track.Clips.Add(a);
        track.Clips.Add(b);
        var lane = new TakeLane
        {
            Takes =
            {
                new Take { ClipId = a.Id, StartBeat = 0, LengthBeats = 4 },
                new Take { ClipId = b.Id, StartBeat = 0, LengthBeats = 4, IsSelected = true }
            }
        };
        track.TakeLanes.Add(lane);

        var promoted = CompEditService.PromoteTake(track, lane);

        Assert.Same(b, promoted);
        Assert.Single(track.Clips);
        Assert.Empty(track.TakeLanes);
    }

    [Fact]
    public void SplitAtPlayhead_DividesCrossingTake()
    {
        var lane = new TakeLane
        {
            Takes =
            {
                new Take { ClipId = Guid.NewGuid(), StartBeat = 0, LengthBeats = 4, IsSelected = true }
            }
        };

        CompEditService.SplitAtPlayhead(lane, 2);

        Assert.Equal(2, lane.Takes.Count);
        Assert.Equal(2, lane.Takes[0].LengthBeats);
        Assert.Equal(2, lane.Takes[1].StartBeat);
        Assert.Equal(2, lane.Takes[1].LengthBeats);
    }

    [Fact]
    public void FlattenComp_ProducesSingleClip()
    {
        var track = new Track { Name = "Vox", Kind = TrackKind.Audio };
        var a = new Clip { Name = "A", IsAudio = true, StartBeat = 0, LengthBeats = 2, Samples = Buffer(2) };
        var b = new Clip { Name = "B", IsAudio = true, StartBeat = 0, LengthBeats = 2, Samples = Buffer(2) };
        track.Clips.Add(a);
        track.Clips.Add(b);
        var lane = new TakeLane
        {
            Takes =
            {
                new Take { ClipId = a.Id, StartBeat = 0, LengthBeats = 2 },
                new Take { ClipId = b.Id, StartBeat = 2, LengthBeats = 2, IsSelected = true }
            }
        };
        track.TakeLanes.Add(lane);

        var flattened = CompEditService.FlattenComp(track, lane, Bpm);

        Assert.NotNull(flattened);
        Assert.Single(track.Clips);
        Assert.Empty(track.TakeLanes);
        Assert.Equal(4, flattened!.LengthBeats);
        Assert.NotNull(flattened.Samples);
    }

    private static AudioSampleBuffer Buffer(int seconds)
    {
        const int rate = 44100;
        var frames = rate * seconds;
        var data = new float[frames * 2];
        for (var i = 0; i < data.Length; i++) data[i] = 0.5f;
        return new AudioSampleBuffer(data, 2, rate);
    }
}
