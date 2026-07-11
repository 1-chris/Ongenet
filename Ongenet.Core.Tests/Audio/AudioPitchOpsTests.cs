using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public class AudioPitchOpsTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void PitchShift_preserves_length_and_channels()
    {
        var tone = Sine(440, 1.0, stereo: true);
        var shifted = AudioPitchOps.PitchShift(tone, 4);
        Assert.Equal(tone.FrameCount, shifted.FrameCount);
        Assert.Equal(tone.Channels, shifted.Channels);
        Assert.Equal(tone.SampleRate, shifted.SampleRate);
    }

    [Fact]
    public void PitchShift_up_octave_doubles_detected_pitch()
    {
        var tone = Sine(440, 2.0);
        var shifted = AudioPitchOps.PitchShift(tone, 12);

        var detector = new PitchDetector();
        detector.Configure(SampleRate, 70.0, 1200.0);

        double f0 = 0;
        var sinceDetect = 0;
        for (long f = 0; f < shifted.FrameCount; f++)
        {
            detector.Push(shifted.Sample(f, 0));
            sinceDetect++;
            if (sinceDetect < 256) continue;
            sinceDetect = 0;
            var detected = detector.Detect();
            if (detected > 0) f0 = detected;
        }

        Assert.InRange(f0, 820, 940);
    }

    [Fact]
    public void PitchShift_up_octave_at_96kHz_doubles_detected_pitch()
    {
        const int sr = 96000;
        var tone = SineAtRate(440, 2.0, sr);
        var shifted = AudioPitchOps.PitchShift(tone, 12);

        var detector = new PitchDetector();
        detector.Configure(sr, 70.0, 1200.0);

        double f0 = 0;
        var sinceDetect = 0;
        for (long f = 0; f < shifted.FrameCount; f++)
        {
            detector.Push(shifted.Sample(f, 0));
            sinceDetect++;
            if (sinceDetect < 512) continue;
            sinceDetect = 0;
            var detected = detector.Detect();
            if (detected > 0) f0 = detected;
        }

        Assert.InRange(f0, 820, 940);
    }

    [Fact]
    public void PitchShift_preserves_tail_energy()
    {
        const int sr = 44100;
        const int frames = sr * 2;
        var samples = new float[frames];
        var tailStart = (int)(frames * 0.85);
        for (var f = 0; f < frames; f++)
        {
            var env = 0.15 + 0.85 * f / frames;
            samples[f] = (float)(0.5 * env * Math.Sin(2 * Math.PI * 440 * f / sr));
        }

        var buf = new AudioSampleBuffer(samples, 1, sr);
        var shifted = AudioPitchOps.PitchShift(buf, 5);

        Assert.Equal(frames, shifted.FrameCount);
        var tailRms = TailRms(shifted, tailStart);
        var headRms = TailRms(shifted, 0, tailStart);
        Assert.True(tailRms > headRms * 0.5, $"Tail RMS {tailRms} vs head {headRms}");
    }

    private static double TailRms(AudioSampleBuffer buffer, int startFrame, int? endFrame = null)
    {
        double sum = 0;
        var end = endFrame ?? (int)buffer.FrameCount;
        var count = end - startFrame;
        if (count <= 0) return 0;
        for (var f = startFrame; f < end; f++)
        {
            var v = buffer.Sample(f, 0);
            sum += v * v;
        }

        return Math.Sqrt(sum / count);
    }

    [Fact]
    public void PitchShift_preserves_exact_sample_count_at_96kHz()
    {
        const int sr = 96000;
        const double seconds = 5.0;
        var tone = SineAtRate(440, seconds, sr);
        var shifted = AudioPitchOps.PitchShift(tone, 5);
        Assert.Equal(tone.Samples.Length, shifted.Samples.Length);
        Assert.Equal(tone.FrameCount, shifted.FrameCount);
        Assert.Equal(seconds, shifted.FrameCount / (double)sr, 3);
    }

    [Fact]
    public void PitchShift_short_sample_still_produces_audio()
    {
        var tone = Sine(440, 0.02); // ~882 frames at 44.1 kHz
        var shifted = AudioPitchOps.PitchShift(tone, 5);
        var max = 0f;
        foreach (var s in shifted.Samples)
            max = Math.Max(max, Math.Abs(s));
        Assert.True(max > 0.01f);
    }

    private static AudioSampleBuffer Sine(double hz, double seconds, bool stereo = false)
        => SineAtRate(hz, seconds, SampleRate, stereo);

    private static AudioSampleBuffer SineAtRate(double hz, double seconds, int sampleRate, bool stereo = false)
    {
        var channels = stereo ? 2 : 1;
        var frames = (int)(seconds * sampleRate);
        var samples = new float[frames * channels];
        for (var f = 0; f < frames; f++)
        {
            var v = (float)(0.5 * Math.Sin(2 * Math.PI * hz * f / sampleRate));
            for (var c = 0; c < channels; c++)
                samples[f * channels + c] = v;
        }

        return new AudioSampleBuffer(samples, channels, sampleRate);
    }
}
