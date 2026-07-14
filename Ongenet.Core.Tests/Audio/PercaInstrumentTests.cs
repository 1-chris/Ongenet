using System;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Tests.Audio;

/// <summary>Exercises the Perca percussion synth: every built-in preset renders a finite, audible
/// one-shot that decays to silence within its preview window.</summary>
public class PercaInstrumentTests
{
    private const int SampleRate = 44100;

    private static float Rms(ReadOnlySpan<float> buffer)
    {
        double sum = 0;
        foreach (var s in buffer) sum += s * (double)s;
        return (float)Math.Sqrt(sum / Math.Max(1, buffer.Length));
    }

    [Theory]
    [InlineData(0)] // Init
    [InlineData(1)] // House Clap
    [InlineData(2)] // Closed Hat
    [InlineData(3)] // Open Hat
    [InlineData(4)] // Dark Snare
    [InlineData(5)] // Crash
    [InlineData(6)] // Rimshot
    [InlineData(15)] // Clave
    public void PresetRendersAudibleDecayingOneShot(int presetIndex)
    {
        var inst = new PercaInstrument();
        inst.LoadPreset(presetIndex);

        var mono = new float[(int)(inst.PreviewSeconds * SampleRate)];
        inst.RenderPreview(mono, SampleRate);

        foreach (var s in mono) Assert.True(float.IsFinite(s), "output must be finite");

        // Audible onset...
        var head = mono.AsSpan(0, SampleRate / 10);
        Assert.True(Rms(head) > 1e-3f, $"preset {presetIndex} should produce an audible hit");

        // ...that has fully decayed by the end of the preview window (the crash rings longest at
        // ~1.75 s; the preview is sized to contain it).
        var tail = mono.AsSpan(mono.Length - mono.Length / 20);
        Assert.True(Rms(tail) < 1e-4f, $"preset {presetIndex} should decay to silence");
    }

    [Fact]
    public void NoteOffIsIgnoredLikeAOneShot()
    {
        var inst = new PercaInstrument();
        inst.LoadPreset(3); // Open Hat: long enough to span several blocks
        inst.Prepare(new Core.Audio.AudioFormat(SampleRate, 2));

        var buffer = new float[512 * 2];
        inst.NoteOn(60, 1.0f);
        Array.Clear(buffer);
        inst.Render(buffer);
        inst.NoteOff(60); // must not cut the hit short

        Array.Clear(buffer);
        inst.Render(buffer);
        Assert.True(Rms(buffer) > 1e-4f, "the one-shot should keep ringing after NoteOff");
    }

    [Fact]
    public void ClonePreservesParameters()
    {
        var inst = new PercaInstrument();
        inst.LoadPreset(1); // House Clap
        var clone = (PercaInstrument)inst.Clone();

        Assert.Equal(inst.Mode, clone.Mode);
        Assert.Equal(inst.Cutoff, clone.Cutoff);
        Assert.Equal(inst.Taps, clone.Taps);
        Assert.Equal(inst.SpreadMs, clone.SpreadMs);
        Assert.Equal(inst.DecayMs, clone.DecayMs);
        Assert.Equal(inst.Width, clone.Width);
    }

    [Fact]
    public void PresetNamesMatchLoadableIndices()
    {
        var inst = new PercaInstrument();
        Assert.Equal(16, inst.PresetNames.Count);
        Assert.Equal("Init", inst.PresetNames[0]);
        Assert.Equal("Clave", inst.PresetNames[^1]);
    }
}
