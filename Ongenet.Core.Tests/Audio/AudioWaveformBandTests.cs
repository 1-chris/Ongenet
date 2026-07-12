using Ongenet.Core.Audio.Files;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class AudioWaveformBandTests
{
    [Fact]
    public void Build_populates_band_peaks()
    {
        const int rate = 44100;
        var frames = rate;
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)rate;
            samples[i] = (float)(Math.Sin(2 * Math.PI * 80 * t) * 0.8
                                 + Math.Sin(2 * Math.PI * 1000 * t) * 0.4
                                 + Math.Sin(2 * Math.PI * 8000 * t) * 0.2);
        }

        var waveform = AudioWaveform.Build(new AudioSampleBuffer(samples, 1, rate), samplesPerBucket: 64);

        Assert.True(waveform.HasBandPeaks);
        waveform.GetBandPeak(WaveformBand.Bass, 0, frames, out _, out var bassMax);
        waveform.GetBandPeak(WaveformBand.Mid, 0, frames, out _, out var midMax);
        waveform.GetBandPeak(WaveformBand.Treble, 0, frames, out _, out var trebleMax);

        Assert.True(bassMax > midMax * 0.5);
        Assert.True(trebleMax > 0.01f);
    }
}
