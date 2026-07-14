using System;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Containers;
using Ongenet.Core.Audio.Instruments;
using Xunit;

namespace Ongenet.Core.Tests.Containers;

public sealed class ContainerInstrumentSmokeTests
{
    private static float Peak(IInstrument inst, int note = 60)
    {
        var format = new AudioFormat(44100, 2);
        inst.Prepare(format);
        inst.NoteOn(note, 0.9f);
        var buf = new float[512];
        inst.Render(buf);
        return buf.Max(Math.Abs);
    }

    [Fact]
    public void InstrumentLayer_ProducesAudioOnKeyboardNote()
    {
        var peak = Peak(new InstrumentLayerInstrument(), 60);
        Assert.True(peak > 1e-4f, $"Expected signal, peak={peak}");
    }

    [Fact]
    public void DrumMachine_ProducesAudioOnPadNote()
    {
        var peak = Peak(new DrumMachineInstrument(), 36);
        Assert.True(peak > 1e-4f, $"Expected signal on pad 36, peak={peak}");
    }

    [Fact]
    public void DrumMachine_ProducesAudioOnKeyboardNote()
    {
        var peak = Peak(new DrumMachineInstrument(), 60);
        Assert.True(peak > 1e-4f, $"Expected signal on keyboard C4, peak={peak}");
    }

    [Fact]
    public void Container_HasActiveVoices_RecursesWithoutStackOverflow()
    {
        var layer = new InstrumentLayerInstrument();
        var format = new AudioFormat(44100, 2);
        layer.Prepare(format);
        layer.NoteOn(60, 0.9f);
        _ = ContainerRenderer.HasActiveVoices(layer);
        layer.NoteOff(60);
    }
}
