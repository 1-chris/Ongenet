using System;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Instruments;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class BuiltInInstrumentSmokeTests
{
    private static float Peak(IInstrument inst, int note = 60)
    {
        var format = new AudioFormat(44100, 2);
        inst.Prepare(format);
        inst.NoteOn(note, 0.9f);
        var buf = new float[512];
        for (var b = 0; b < 4; b++)
        {
            Array.Clear(buf);
            inst.Render(buf);
        }

        inst.NoteOff(note);
        return buf.Max(Math.Abs);
    }

    [Theory]
    [InlineData(typeof(PolysynthInstrument), 60)]
    [InlineData(typeof(PolymerInstrument), 60)]
    [InlineData(typeof(OrganInstrument), 60)]
    [InlineData(typeof(Phase4Instrument), 60)]
    [InlineData(typeof(DrumModelInstrument), 36)]
    public void RegistryInstrument_RendersFiniteAudio(Type type, int note)
    {
        var inst = (IInstrument)Activator.CreateInstance(type)!;
        var peak = Peak(inst, note);
        Assert.True(peak > 1e-4f, $"{type.Name} produced near-silence (peak={peak})");
    }

    [Fact]
    public void DrumModelFactoryPresets_TargetExpectedModels()
    {
        Assert.Equal(26, ((DrumModelInstrument)FactoryPresets.Definitions.First(d => d.PresetName == "808 Kick").Create()).Model);
        Assert.Equal(29, ((DrumModelInstrument)FactoryPresets.Definitions.First(d => d.PresetName == "909 Snare").Create()).Model);
        Assert.Equal(24, ((DrumModelInstrument)FactoryPresets.Definitions.First(d => d.PresetName == "Tight Hat").Create()).Model);
        Assert.Equal(22, ((DrumModelInstrument)FactoryPresets.Definitions.First(d => d.PresetName == "Room Clap").Create()).Model);
        Assert.Equal(5, ((DrumModelInstrument)FactoryPresets.Definitions.First(d => d.PresetName == "Techno Zap").Create()).Model);
        Assert.Equal(30, ((DrumModelInstrument)FactoryPresets.Definitions.First(d => d.PresetName == "Lo-Fi Tom").Create()).Model);
        Assert.Equal(0, ((DrumModelInstrument)FactoryPresets.Definitions.First(d => d.PresetName == "Vintage Cymbal").Create()).Model);
        Assert.Equal(28, ((DrumModelInstrument)FactoryPresets.Definitions.First(d => d.PresetName == "Punch Rim").Create()).Model);
    }

    [Fact]
    public void PolysynthAndPolymer_HaveFactoryPresets()
    {
        Assert.Equal(6, FactoryPresets.Definitions.Count(d => d.InstrumentDisplayName == "Polysynth"));
        Assert.Equal(5, FactoryPresets.Definitions.Count(d => d.InstrumentDisplayName == "Polymer"));
    }
}
