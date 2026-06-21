using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Library;

public class PresetFileTests
{
    private static readonly IInstrumentRegistry Instruments = new InstrumentRegistry();
    private static readonly IEffectRegistry Effects = new EffectRegistry();

    [Fact]
    public void InstrumentPreset_RoundTripsParameters()
    {
        var inst = Instruments.Create(KickaInstrument.TypeId);
        var floatParam = inst.Parameters.OfType<FloatParameter>().First();
        var target = (floatParam.Min + floatParam.Max) / 2 + (floatParam.Max - floatParam.Min) * 0.1;
        floatParam.Value = target;

        using var ms = new MemoryStream();
        PresetFile.SaveInstrument(inst, "My Kick", "tester", ms);
        ms.Position = 0;

        var meta = PresetFile.ReadMeta(ms)!;
        Assert.Equal(PresetKind.Instrument, meta.Kind);
        Assert.Equal(KickaInstrument.TypeId, meta.TypeId);
        Assert.Equal("My Kick", meta.DisplayName);

        ms.Position = 0;
        var result = PresetFile.Load(ms, Instruments, Effects)!;
        var loaded = Assert.IsType<KickaInstrument>(result.Instrument);
        var loadedParam = loaded.Parameters.OfType<FloatParameter>().First();
        Assert.Equal(target, loadedParam.Value, 4);
    }

    [Fact]
    public void InstrumentPreset_EmbedsAndRestoresSample()
    {
        var inst = Instruments.Create(BasicSamplerInstrument.TypeId);
        var host = Assert.IsAssignableFrom<ISampleHost>(inst);
        var buffer = new AudioSampleBuffer(Enumerable.Repeat(0.5f, 128).ToArray(), 1, 44100);
        host.LoadSample(buffer, "snare");

        using var ms = new MemoryStream();
        PresetFile.SaveInstrument(inst, "Snare", "tester", ms);
        ms.Position = 0;

        var result = PresetFile.Load(ms, Instruments, Effects)!;
        var loadedHost = Assert.IsAssignableFrom<ISampleHost>(result.Instrument);
        Assert.Equal("snare", loadedHost.SampleName);
        Assert.NotNull(loadedHost.CurrentSample);
        Assert.Equal(128, loadedHost.CurrentSample!.FrameCount);
    }

    [Fact]
    public void ChainPreset_RoundTripsEffectsInOrder()
    {
        var ids = Effects.Available.Take(3).Select(e => e.Id).ToList();
        if (ids.Count < 2) return; // need at least two effect types
        var chain = ids.Select(id => Effects.Create(id)).ToList();
        // Bypass the middle one to prove per-effect enabled state survives.
        if (chain.Count >= 2) chain[1].Enabled = false;

        using var ms = new MemoryStream();
        PresetFile.SaveChain(chain, "My Chain", "tester", ms);
        ms.Position = 0;

        var meta = PresetFile.ReadMeta(ms)!;
        Assert.Equal(PresetKind.EffectChain, meta.Kind);

        ms.Position = 0;
        var result = PresetFile.Load(ms, Instruments, Effects)!;
        Assert.NotNull(result.Effects);
        Assert.Equal(chain.Count, result.Effects!.Count);
        for (var i = 0; i < chain.Count; i++)
            Assert.Equal(chain[i].TypeId, result.Effects[i].TypeId);
        if (chain.Count >= 2) Assert.False(result.Effects[1].Enabled);
    }

    [Fact]
    public void EffectPreset_RoundTripsParameters()
    {
        var fx = Effects.Create(Effects.Available.First().Id);
        var floatParam = fx.Parameters.OfType<FloatParameter>().FirstOrDefault();
        if (floatParam is null) return; // effect has no float params to check
        var target = (floatParam.Min + floatParam.Max) / 2;
        floatParam.Value = target;

        using var ms = new MemoryStream();
        PresetFile.SaveEffect(fx, "My FX", "tester", ms);
        ms.Position = 0;

        var result = PresetFile.Load(ms, Instruments, Effects)!;
        Assert.Equal(PresetKind.Effect, result.Meta.Kind);
        Assert.NotNull(result.Effect);
        var loadedParam = result.Effect!.Parameters.OfType<FloatParameter>().First();
        Assert.Equal(target, loadedParam.Value, 4);
    }
}
