using System;
using System.Collections.Generic;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments.Sfz;

namespace Ongenet.Core.Tests.Sfz;

public class SfzToneTests
{
    private static SfzInstrument Make(string sfz, string name, AudioSampleBuffer buf)
    {
        var doc = SfzParser.Parse(sfz);
        var inst = new SfzInstrument();
        inst.Prepare(new AudioFormat(44100, 1));
        inst.ApplyLoad(new SfzLoadResult
        {
            Document = doc,
            Library = new SfzSampleLibrary(new Dictionary<string, SfzSample> { [name] = SfzSample.FromResident(buf) }),
            SfzPath = "t.sfz",
            SfzText = sfz,
            DisplayName = "t"
        });
        return inst;
    }

    // A maximal-frequency signal (alternating +/-amp) that a low-pass should strongly attenuate.
    private static AudioSampleBuffer Alternating(float amp, int frames)
    {
        var s = new float[frames];
        for (var i = 0; i < frames; i++) s[i] = i % 2 == 0 ? amp : -amp;
        return new AudioSampleBuffer(s, 1, 44100);
    }

    private static double Rms(float[] b, int start, int count)
    {
        double sum = 0;
        for (var i = start; i < start + count; i++) sum += b[i] * (double)b[i];
        return Math.Sqrt(sum / count);
    }

    [Fact]
    public void LowPassAttenuatesHighFrequencyContent()
    {
        var open = Make("<region> sample=a.wav key=60 pitch_keycenter=60", "a.wav", Alternating(0.5f, 400));
        open.NoteOn(60, 1f);
        var openBuf = new float[400];
        open.Render(openBuf);
        var openRms = Rms(openBuf, 100, 200);

        var filtered = Make("<region> sample=a.wav key=60 pitch_keycenter=60 cutoff=300 fil_type=lpf_2p",
            "a.wav", Alternating(0.5f, 400));
        filtered.NoteOn(60, 1f);
        var filtBuf = new float[400];
        filtered.Render(filtBuf);
        var filtRms = Rms(filtBuf, 100, 200);

        Assert.True(openRms > 0.3); // unfiltered Nyquist tone is loud
        Assert.True(filtRms < openRms * 0.25, $"filtered={filtRms:F4} open={openRms:F4}");
    }

    [Fact]
    public void AmpLfoProducesTremolo()
    {
        var inst = Make("<region> sample=a.wav key=60 pitch_keycenter=60 amp_veltrack=0 amplfo_freq=8 amplfo_depth=12",
            "a.wav", ConstBuf(0.5f, 44100));
        inst.NoteOn(60, 1f);

        var buf = new float[8000];
        inst.Render(buf);

        float min = float.MaxValue, max = 0f;
        foreach (var s in buf)
        {
            var a = Math.Abs(s);
            if (a > max) max = a;
            if (a < min) min = a;
        }

        Assert.True(max > min * 1.5f, $"min={min:F4} max={max:F4}"); // amplitude clearly oscillates
    }

    private static AudioSampleBuffer ConstBuf(float v, int frames)
    {
        var s = new float[frames];
        for (var i = 0; i < frames; i++) s[i] = v;
        return new AudioSampleBuffer(s, 1, 44100);
    }
}
