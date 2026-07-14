using System;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Tests.Effects;

public class ExciterEffectTests
{
    private static float[] SineStereo(double freq, int frames, double sr, float amp = 0.5f)
    {
        var buf = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var s = (float)(Math.Sin(2 * Math.PI * freq * i / sr) * amp);
            buf[i * 2] = s;
            buf[i * 2 + 1] = s;
        }

        return buf;
    }

    private static double Rms(ReadOnlySpan<float> buf)
    {
        double sum = 0;
        for (var i = 0; i < buf.Length; i++) sum += buf[i] * (double)buf[i];
        return Math.Sqrt(sum / buf.Length);
    }

    [Fact]
    public void ProcessAddsHighBandEnergyOnBrightSource()
    {
        const double sr = 48000;
        var fx = new ExciterEffect
        {
            Drive = 12,
            Mix = 0.8,
            ToneHz = 2000,
            Mode = (int)ShaperType.Tanh,
            OutputDb = 0
        };
        fx.Prepare(new AudioFormat((int)sr, 2));

        // Mid-high tone above the high-pass so the shaper has something to enhance.
        var buf = SineStereo(4000, 4096, sr, 0.4f);
        var before = Rms(buf);
        fx.Process(buf);
        var after = Rms(buf);

        foreach (var s in buf)
            Assert.True(float.IsFinite(s), "output must be finite");
        Assert.True(after > before * 0.5, "exciter should keep audible energy");
        Assert.True(after != before, "mix > 0 should alter the signal");
    }

    [Fact]
    public void ClonePreservesSettings()
    {
        var fx = new ExciterEffect
        {
            Drive = 8, Mix = 0.4, ToneHz = 5000, Mode = 2, OutputDb = -1, Enabled = true
        };
        var clone = (ExciterEffect)fx.Clone();
        Assert.Equal(fx.Drive, clone.Drive);
        Assert.Equal(fx.Mix, clone.Mix);
        Assert.Equal(fx.ToneHz, clone.ToneHz);
        Assert.Equal(fx.Mode, clone.Mode);
        Assert.Equal(fx.OutputDb, clone.OutputDb);
    }
}
