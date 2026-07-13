using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class VideoWaveformCacheServiceTests
{
    private static readonly AudioFormat Format = new(48000, 2);

    [Fact]
    public void GroupTrack_ProducesNonEmptyWaveform()
    {
        var project = new Project { Name = "WF", BarCount = 4, Tempo = new Tempo(120) };
        project.Tracks.Add(new Track { Name = "Master", Kind = TrackKind.Master });
        var group = new Track { Name = "Drums", Kind = TrackKind.Group };
        var child = new Track { Name = "Kick", Kind = TrackKind.Audio, ParentId = group.Id };
        project.Tracks.Add(group);
        project.Tracks.Add(child);

        var frames = (int)(Format.SampleRate * 0.5);
        var data = new float[frames * Format.Channels];
        for (var i = 0; i < frames; i++)
        {
            var s = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / Format.SampleRate));
            data[i * 2] = s;
            data[i * 2 + 1] = s;
        }

        child.Clips.Add(new Clip
        {
            Name = "Kick",
            IsAudio = true,
            StartBeat = 0,
            LengthBeats = 4,
            Samples = new AudioSampleBuffer(data, Format.Channels, Format.SampleRate)
        });

        var cache = new VideoWaveformCacheService();
        var wf = cache.GetOrBuild(project, group.Id, 120);

        Assert.NotNull(wf);
        Assert.True(wf.TotalFrames > 0);
        Assert.True(wf.BucketCount > 0);
        wf.GetPeak(0, wf.TotalFrames, out _, out var max);
        Assert.True(max > 0);
    }
}
