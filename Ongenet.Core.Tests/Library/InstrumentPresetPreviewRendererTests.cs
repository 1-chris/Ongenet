using System.IO;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Library;

public sealed class InstrumentPresetPreviewRendererTests
{
    [Fact]
    public void RendersKickaPresetPreview()
    {
        var inst = new InstrumentRegistry().Create(KickaInstrument.TypeId);
        var buffer = InstrumentPresetPreviewRenderer.Render(inst);
        Assert.NotNull(buffer);
        Assert.True(buffer!.FrameCount > 0);
        Assert.Contains(buffer.Samples, s => System.Math.Abs(s) > 1e-4f);
    }

    [Fact]
    public void PresetMetaV2_RoundTripsTags()
    {
        var inst = new InstrumentRegistry().Create(TripleOscInstrument.TypeId);
        using var ms = new MemoryStream();
        PresetFile.SaveInstrument(inst, "Tagged", "tester", ms, ["bass", "sub"]);
        ms.Position = 0;
        var meta = PresetFile.ReadMeta(ms)!;
        Assert.Contains("bass", meta.Tags);
        Assert.Contains("sub", meta.Tags);
    }
}
