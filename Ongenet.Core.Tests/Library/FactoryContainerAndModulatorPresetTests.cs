using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Containers;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Modulation;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Library;

public sealed class FactoryContainerAndModulatorPresetTests
{
    [Fact]
    public void ContainerDefinitions_CreateAndRoundTrip()
    {
        Assert.Equal(3, FactoryContainerPresets.Definitions.Count);
        foreach (var def in FactoryContainerPresets.Definitions)
        {
            var fx = def.Create();
            Assert.NotNull(fx);
            using var ms = new MemoryStream();
            PresetFile.SaveEffect(fx, def.PresetName, "Factory", ms, def.Tags);
            Assert.True(ms.Length > 0);
        }
    }

    [Fact]
    public void ModulatorDefinitions_CreateAndRoundTrip()
    {
        Assert.Equal(3, FactoryModulatorPresets.Definitions.Count);
        foreach (var def in FactoryModulatorPresets.Definitions)
        {
            var slots = def.Create();
            Assert.NotEmpty(slots);
            using var ms = new MemoryStream();
            PresetFile.SaveModulatorChain(slots, def.PresetName, "Factory", ms, def.Tags);
            ms.Position = 0;
            var meta = PresetFile.ReadMeta(ms)!;
            Assert.Equal(PresetKind.ModulatorChain, meta.Kind);
            Assert.NotEmpty(meta.Tags);
            ms.Position = 0;
            var loaded = PresetFile.Load(ms, new InstrumentRegistry(), new EffectRegistry(), new ModulatorRegistry())!;
            Assert.NotNull(loaded.ModulatorSlots);
            Assert.Equal(slots.Count, loaded.ModulatorSlots!.Count);
        }
    }

    [Fact]
    public void DistructorFxLayer_HasDistortionFilterAmpBranches()
    {
        var fx = Assert.IsType<FxLayerEffect>(FactoryContainerPresets.Definitions
            .First(d => d.PresetName == "Distructor").Create());
        Assert.Equal(2, fx.Branches.Count);
        Assert.Equal("distortion", fx.Branches[0].Effects[0].TypeId);
        Assert.Equal("filter", fx.Branches[0].Effects[1].TypeId);
        Assert.Equal("amp", fx.Branches[0].Effects[2].TypeId);
    }

    [Fact]
    public void TriBandDelays_HasDelayOnEachBand()
    {
        var fx = Assert.IsType<MultibandFxEffect>(FactoryContainerPresets.Definitions
            .First(d => d.PresetName == "Tri-Band Delays").Create());
        Assert.Equal(3, fx.Branches.Count);
        foreach (var branch in fx.Branches)
            Assert.Equal("delay", branch.Effects[0].TypeId);
    }
}
