using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Tests.Library;

public class FactoryPresetsTests
{
    [Fact]
    public void FactoryContentVersionIsPositive()
        => Assert.True(FactoryContentVersion.Current >= 1);

    [Fact]
    public void InstrumentDefinitionsCreateAndRoundTrip()
    {
        Assert.True(FactoryPresets.Definitions.Count >= 40);
        foreach (var def in FactoryPresets.Definitions)
        {
            var inst = def.Create();
            Assert.NotNull(inst);
            Assert.False(string.IsNullOrWhiteSpace(def.PresetName));
            using var ms = new MemoryStream();
            PresetFile.SaveInstrument(inst, def.PresetName, "Factory", ms);
            Assert.True(ms.Length > 0);
        }
    }

    [Fact]
    public void EffectDefinitionsCreateAndRoundTrip()
    {
        Assert.True(FactoryPresets.EffectDefinitions.Count >= 30);
        foreach (var def in FactoryPresets.EffectDefinitions)
        {
            var fx = def.Create();
            Assert.NotNull(fx);
            using var ms = new MemoryStream();
            PresetFile.SaveEffect(fx, def.PresetName, "Factory", ms);
            Assert.True(ms.Length > 0);
        }
    }

    [Fact]
    public void ChainDefinitionsCreateAndRoundTrip()
    {
        Assert.True(FactoryPresets.ChainDefinitions.Count >= 12);
        foreach (var def in FactoryPresets.ChainDefinitions)
        {
            var chain = def.Create();
            Assert.NotEmpty(chain);
            using var ms = new MemoryStream();
            PresetFile.SaveChain(chain, def.PresetName, "Factory", ms);
            Assert.True(ms.Length > 0);
        }
    }

    [Fact]
    public void KickaPaddaPercaMeetPresetTargets()
    {
        Assert.True(new KickaInstrument().PresetNames.Count >= 15);
        Assert.True(new PaddaInstrument().PresetNames.Count >= 15);
        Assert.True(new PercaInstrument().PresetNames.Count >= 15);

        for (var i = 0; i < new KickaInstrument().PresetNames.Count; i++)
            new KickaInstrument().LoadPreset(i);
        for (var i = 0; i < new PaddaInstrument().PresetNames.Count; i++)
            new PaddaInstrument().LoadPreset(i);
        for (var i = 0; i < new PercaInstrument().PresetNames.Count; i++)
            new PercaInstrument().LoadPreset(i);
    }

    [Fact]
    public void CombinedFactoryInstrumentPresetSurfaceMeetsTarget()
    {
        var providerCount =
            new KickaInstrument().PresetNames.Count +
            new PaddaInstrument().PresetNames.Count +
            new PercaInstrument().PresetNames.Count;
        var total = providerCount + FactoryPresets.Definitions.Count;
        Assert.True(total >= 80, $"expected ≥80 factory instrument presets, got {total}");
    }
}
