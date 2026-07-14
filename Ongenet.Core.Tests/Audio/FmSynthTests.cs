using System;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Instruments;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class FmSynthTests
{
    [Fact]
    public void FourOperatorMatrix_ProducesAudio()
    {
        var fm = new FmSynthInstrument();
        fm.LoadPreset(9); // DX Stack
        var format = new AudioFormat(44100, 2);
        fm.Prepare(format);
        fm.NoteOn(60, 0.9f);
        var buf = new float[512];
        fm.Render(buf);
        Assert.True(buf.Max(Math.Abs) > 1e-4f);
        fm.NoteOff(60);
    }

    [Fact]
    public void Presets_LoadWithoutThrowing()
    {
        var fm = new FmSynthInstrument();
        for (var i = 0; i < fm.PresetNames.Count; i++)
            fm.LoadPreset(i);
    }

    [Fact]
    public void Clone_PreservesMatrix()
    {
        var fm = new FmSynthInstrument();
        fm.Matrix[2, 1] = 0.42;
        fm.Operators[3].Ratio = 3.5;
        var clone = (FmSynthInstrument)fm.Clone();
        Assert.Equal(0.42, clone.Matrix[2, 1], 3);
        Assert.Equal(3.5, clone.Operators[3].Ratio, 3);
    }
}
