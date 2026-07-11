using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;
using Xunit;
using Xunit.Abstractions;

namespace Ongenet.Core.Tests.Audio;

public class RubberBandStretcherDiagTests
{
    private readonly ITestOutputHelper _out;

    public RubberBandStretcherDiagTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void PitchShift_reports_nonzero_energy_for_realistic_buffers()
    {
        const int sr = 44100;
        foreach (var seconds in new[] { 0.5, 2.0, 5.0 })
        {
            foreach (var stereo in new[] { false, true })
            {
                var buf = NoiseBurst(sr, seconds, stereo);
                var inRms = Rms(buf);
                var shifted = AudioPitchOps.PitchShift(buf, 5);
                var outRms = Rms(shifted);
                var max = MaxAbs(shifted);
                _out.WriteLine($"{seconds}s stereo={stereo}: inRms={inRms:F6} outRms={outRms:F6} max={max:F6} nan={CountNaN(shifted)} nearZero={CountNearZero(shifted, 1e-6f)}/{shifted.Samples.Length}");
                Assert.True(outRms > 1e-4, $"Output too quiet for {seconds}s stereo={stereo}");
                Assert.True(max > 1e-4, $"Output max too small for {seconds}s stereo={stereo}");
                Assert.True(max < 2.0, $"Output clipped/runaway for {seconds}s stereo={stereo}");
                Assert.InRange(outRms / inRms, 0.05, 4.0);
            }
        }
    }

    private static AudioSampleBuffer NoiseBurst(int sr, double seconds, bool stereo)
    {
        var ch = stereo ? 2 : 1;
        var frames = (int)(seconds * sr);
        var samples = new float[frames * ch];
        var rng = new Random(42);
        for (var f = 0; f < frames; f++)
        {
            var env = f < sr / 20 ? 1f : (float)Math.Exp(-(f - sr / 20.0) / (sr * 0.5));
            var v = (float)(rng.NextDouble() * 2 - 1) * env * 0.5f;
            for (var c = 0; c < ch; c++)
                samples[f * ch + c] = v;
        }

        return new AudioSampleBuffer(samples, ch, sr);
    }

    private static double Rms(AudioSampleBuffer b)
    {
        double s = 0;
        var n = b.Samples.Length;
        for (var i = 0; i < n; i++) s += b.Samples[i] * b.Samples[i];
        return Math.Sqrt(s / n);
    }

    private static float MaxAbs(AudioSampleBuffer b)
    {
        var m = 0f;
        foreach (var x in b.Samples) m = Math.Max(m, Math.Abs(x));
        return m;
    }

    private static int CountNaN(AudioSampleBuffer b)
    {
        var n = 0;
        foreach (var x in b.Samples) if (float.IsNaN(x)) n++;
        return n;
    }

    private static int CountNearZero(AudioSampleBuffer b, float eps)
    {
        var n = 0;
        foreach (var x in b.Samples) if (Math.Abs(x) < eps) n++;
        return n;
    }
}
