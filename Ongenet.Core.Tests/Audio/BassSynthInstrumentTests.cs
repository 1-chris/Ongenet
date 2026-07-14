using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Tests.Audio;

public class BassSynthInstrumentTests
{
    [Fact]
    public void LoadPresetAppliesNamedPatches()
    {
        var inst = new BassSynthInstrument();
        Assert.Equal(9, inst.PresetNames.Count);
        Assert.Equal("Init", inst.PresetNames[0]);
        Assert.Equal("Funky Slap", inst.PresetNames[^1]);

        inst.LoadPreset(1); // Deep Sub
        Assert.Equal(0, inst.Wave); // Sine
        Assert.True(inst.SubLevel > 0.7);
        Assert.True(inst.Cutoff < 400);

        inst.LoadPreset(2); // Reese
        Assert.True(inst.Unison >= 3);
        Assert.True(inst.Drive > 0.2);

        var clone = (BassSynthInstrument)inst.Clone();
        Assert.Equal(inst.Wave, clone.Wave);
        Assert.Equal(inst.Unison, clone.Unison);
        Assert.Equal(inst.Cutoff, clone.Cutoff);
        Assert.Equal(inst.Drive, clone.Drive);
        Assert.Equal(inst.FilterEnvAmount, clone.FilterEnvAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    public void LoadPresetAcceptsAllIndices(int index)
    {
        var inst = new BassSynthInstrument();
        inst.LoadPreset(index);
        Assert.InRange(inst.Gain, 0, 1);
        Assert.InRange(inst.Unison, 1, 7);
    }
}
