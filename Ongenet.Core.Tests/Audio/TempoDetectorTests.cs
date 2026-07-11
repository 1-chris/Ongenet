using System;
using Ongenet.Core.Audio.Files;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public class TempoDetectorTests
{
    [Fact]
    public void Estimate_detects_click_track_near_120_bpm()
    {
        const int sr = 44100;
        const double targetBpm = 120.0;
        const double seconds = 8.0;
        var buffer = BuildClickTrack(sr, seconds, targetBpm);
        var detected = TempoDetector.Estimate(buffer);
        Assert.NotNull(detected);
        Assert.InRange(detected!.Value, 115, 125);
    }

    [Fact]
    public void Estimate_with_hint_stays_near_hint_after_pitch_shift()
    {
        const int sr = 44100;
        const double targetBpm = 122.0;
        const double seconds = 10.0;
        var buffer = BuildClickTrack(sr, seconds, targetBpm);
        var shifted = AudioPitchOps.PitchShift(buffer, 5);

        var withoutHint = TempoDetector.Estimate(shifted);
        var withHint = TempoDetector.Estimate(shifted, targetBpm);

        Assert.NotNull(withoutHint);
        Assert.NotNull(withHint);
        Assert.InRange(withHint!.Value, targetBpm - 3, targetBpm + 3);
    }

    [Fact]
    public void FromPath_reads_bpm_tag()
    {
        var bpm = TempoDetector.FromPath("/Loops/122bpm/vocal.wav");
        Assert.Equal(122.0, bpm);
    }

    private static AudioSampleBuffer BuildClickTrack(int sampleRate, double seconds, double bpm)
    {
        var frames = (int)(seconds * sampleRate);
        var samples = new float[frames];
        var interval = sampleRate * 60.0 / bpm;
        var next = 0.0;
        for (var f = 0; f < frames; f++)
        {
            if (f >= next)
            {
                samples[f] = 0.9f;
                next += interval;
            }
        }

        return new AudioSampleBuffer(samples, 1, sampleRate);
    }
}
