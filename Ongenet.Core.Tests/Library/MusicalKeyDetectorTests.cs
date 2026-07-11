using System;
using Ongenet.Core.Audio.Files;
using Xunit;

namespace Ongenet.Core.Tests.Library;

public class MusicalKeyDetectorTests
{
    private const int Sr = 44100;

    private static AudioSampleBuffer Tones(double[] freqs, double seconds = 2.0)
    {
        var n = (int)(seconds * Sr);
        var s = new float[n];
        for (var i = 0; i < n; i++)
        {
            double v = 0;
            foreach (var f in freqs) v += Math.Sin(2 * Math.PI * f * i / Sr);
            s[i] = (float)(v / freqs.Length) * 0.5f;
        }

        return new AudioSampleBuffer(s, 1, Sr);
    }

    [Fact]
    public void Silence_ReturnsEmpty()
        => Assert.Equal(string.Empty, MusicalKeyDetector.Detect(new AudioSampleBuffer(new float[Sr], 1, Sr)));

    [Fact]
    public void TooShort_ReturnsEmpty()
        => Assert.Equal(string.Empty, MusicalKeyDetector.Detect(new AudioSampleBuffer(new float[100], 1, Sr)));

    [Fact]
    public void CMajorChord_DetectsCOrRelativeMinor()
    {
        var key = MusicalKeyDetector.Detect(Tones(new[] { 261.63, 329.63, 392.00 }, seconds: 12.0));
        Assert.False(string.IsNullOrEmpty(key));
        Assert.True(key is "C maj" or "A min", $"Expected C maj or A min, got {key}");
    }
}
