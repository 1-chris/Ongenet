using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Tests.Audio;

public class CrossfadeTests
{
    private static Clip Audio(double start, double length) =>
        new() { IsAudio = true, StartBeat = start, LengthBeats = length };

    [Fact]
    public void NonOverlappingClipsGetNoFades()
    {
        var a = Audio(0, 4);
        var b = Audio(4, 4); // abuts but does not overlap
        var fades = Crossfade.Compute(new[] { a, b });

        Assert.Equal((0.0, 0.0), fades[a]);
        Assert.Equal((0.0, 0.0), fades[b]);
    }

    [Fact]
    public void OverlapFadesOutTheEarlierAndInTheLater()
    {
        var a = Audio(0, 4);
        var b = Audio(3, 4); // overlaps a by 1 beat
        var fades = Crossfade.Compute(new[] { b, a }); // unsorted input

        Assert.Equal(0.0, fades[a].FadeInBeats);
        Assert.Equal(1.0, fades[a].FadeOutBeats, 9);
        Assert.Equal(1.0, fades[b].FadeInBeats, 9);
        Assert.Equal(0.0, fades[b].FadeOutBeats);
    }

    [Fact]
    public void OverlapIsClampedToTheShorterClip()
    {
        var a = Audio(0, 4);
        var b = Audio(1, 0.5); // fully inside a; overlap can't exceed b's length
        var fades = Crossfade.Compute(new[] { a, b });

        Assert.Equal(0.5, fades[a].FadeOutBeats, 9);
        Assert.Equal(0.5, fades[b].FadeInBeats, 9);
    }

    [Fact]
    public void NonAudioClipsAreIgnored()
    {
        var a = Audio(0, 4);
        var midi = new Clip { IsAudio = false, StartBeat = 3, LengthBeats = 4 };
        var fades = Crossfade.Compute(new[] { a, midi });

        Assert.Equal((0.0, 0.0), fades[a]);
        Assert.False(fades.ContainsKey(midi));
    }

    [Fact]
    public void OverlapWaveformMixesTheTwoClipsInTheOverlapRegion()
    {
        // Two 4-beat clips of constant-1 samples overlapping by 2 beats at 120 BPM. The linear crossfade
        // sums to ~1 across the overlap, so the mixed preview is non-silent.
        var rate = 8000;
        var ones = new float[rate * 2]; // 2 seconds = 4 beats at 120 BPM
        for (var i = 0; i < ones.Length; i++) ones[i] = 1f;

        var a = new Clip { IsAudio = true, StartBeat = 0, LengthBeats = 4, Samples = new AudioSampleBuffer(ones, 1, rate) };
        var b = new Clip { IsAudio = true, StartBeat = 2, LengthBeats = 4, Samples = new AudioSampleBuffer(ones, 1, rate) };

        var wave = Crossfade.OverlapWaveform(a, b, crossfadeBeats: 2, projectBpm: 120);

        Assert.NotNull(wave);
        wave!.GetPeak(0, wave.TotalFrames, out _, out var max);
        Assert.True(max > 0.5f, $"expected an audible mixed peak, got {max}");
    }

    [Fact]
    public void OverlapWaveformIsNullWithoutOverlap()
    {
        var rate = 8000;
        var buf = new AudioSampleBuffer(new float[rate], 1, rate);
        var a = new Clip { IsAudio = true, StartBeat = 0, LengthBeats = 4, Samples = buf };
        var b = new Clip { IsAudio = true, StartBeat = 4, LengthBeats = 4, Samples = buf };
        Assert.Null(Crossfade.OverlapWaveform(a, b, crossfadeBeats: 0, projectBpm: 120));
    }

    [Fact]
    public void GainRampsLinearlyAndComplementsAcrossACrossfade()
    {
        // A clip of length 4 fading out over its last 1 beat, and a clip fading in over its first 1 beat.
        // Across the overlap the two linear gains sum to 1 (constant amplitude).
        const double len = 4, fade = 1;
        for (var t = 0.0; t <= 1.0; t += 0.25)
        {
            var outGain = Crossfade.Gain(len - fade + t, len, 0, fade);   // earlier clip fading out
            var inGain = Crossfade.Gain(t, len, fade, 0);                 // later clip fading in
            Assert.Equal(1.0f, outGain + inGain, 5);
        }

        Assert.Equal(1.0f, Crossfade.Gain(2, len, fade, fade));  // middle: no fade
        Assert.Equal(0.0f, Crossfade.Gain(0, len, fade, 0));     // very start of a fade-in
    }
}
