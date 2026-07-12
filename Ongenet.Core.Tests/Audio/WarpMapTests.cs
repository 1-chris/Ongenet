using Ongenet.Core.Audio;
using Ongenet.Core.Models.Audio;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class WarpMapTests
{
    [Fact]
    public void Segment_ReturnsExpectedSourceSecondsAtMarkerBoundary()
    {
        var clip = new Clip
        {
            LengthBeats = 8,
            SourceOffsetSeconds = 0,
            StretchToTempo = true,
            WarpMarkers =
            {
                new WarpMarker { BeatPosition = 4, SourceSeconds = 3 }
            }
        };

        var warp = WarpMap.FromClip(clip, sourceEndSeconds: 6);
        var (b0, b1, s0, s1) = warp.Segment(0);
        Assert.Equal(0, b0);
        Assert.Equal(4, b1);
        Assert.Equal(0, s0, 3);
        Assert.Equal(3, s1, 3);

        var (b0b, b1b, s0b, s1b) = warp.Segment(1);
        Assert.Equal(4, b0b);
        Assert.Equal(8, b1b);
        Assert.Equal(3, s0b, 3);
        Assert.Equal(6, s1b, 3);
    }

    [Fact]
    public void BeatToSource_InterpolatesWithinSegment()
    {
        var clip = new Clip
        {
            LengthBeats = 4,
            SourceOffsetSeconds = 0,
            WarpMarkers = { new WarpMarker { BeatPosition = 2, SourceSeconds = 3 } }
        };
        var warp = WarpMap.FromClip(clip, 6);
        Assert.Equal(1.5, warp.BeatToSource(1), 3);
        Assert.Equal(4.5, warp.BeatToSource(3), 3);
    }
}
