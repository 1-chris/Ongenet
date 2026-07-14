using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Tests.Audio;

public sealed class BeatGridWarpOpsTests
{
    [Fact]
    public void ApplyBeatGridWarp_stretches_short_segments_to_one_beat()
    {
        var sampleRate = 44100;
        var beatFrames = sampleRate / 2;
        var quarterBeat = beatFrames / 2;
        var samples = new float[quarterBeat * 4];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (float)Math.Sin(i * 0.01);
        var buffer = new AudioSampleBuffer(samples, 1, sampleRate);

        var warped = BeatGridWarpOps.ApplyBeatGridWarp(buffer, secondsPerBeat: 0.5, beatsPerSegment: 1.0,
            transientSafe: false);

        Assert.Equal(beatFrames * 2, warped.FrameCount);
    }

    [Fact]
    public void BuildBeatGridSegments_uses_slice_regions_when_present()
    {
        var buffer = new AudioSampleBuffer(new float[8000], 1, 4000);
        buffer.SliceRegions.Add(new AudioSliceRegion { StartFrame = 0, EndFrame = 2000, Order = 0 });
        buffer.SliceRegions.Add(new AudioSliceRegion { StartFrame = 2000, EndFrame = 4000, Order = 1 });

        var segments = BeatGridWarpOps.BuildBeatGridSegments(buffer, secondsPerBeat: 0.5);
        Assert.Equal(2, segments.Count);
        Assert.Equal(2000, segments[1].SourceStartFrame);
    }
}
