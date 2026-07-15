using System;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Music;

namespace Ongenet.Core.Tests.Music;

/// <summary>
/// Asserts each built-in song factory uses the intended mastering chain and keeps
/// time-varying FX before the Peak Limiter on Master.
/// </summary>
public class MasteringFactoryChainTests
{
    private static IInstrumentRegistry Registry()
    {
        var instruments = new InstrumentRegistry();
        FieldBootstrap.Initialize(new FieldNodeRegistry(), instruments, new EffectRegistry());
        return instruments;
    }

    private static void AssertMasterMatchesRecipe(Project song, string recipeName, params Type[] trailingExtras)
    {
        var expected = MasteringChains.Create(recipeName).ToList();
        expected.AddRange(trailingExtras.Select(t => (IAudioEffect)Activator.CreateInstance(t)!));
        var master = song.Master!.Effects;
        Assert.True(master.Count >= expected.Count,
            $"{song.Name}: expected at least {expected.Count} master inserts, got {master.Count}");
        for (var i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i].GetType(), master[i].GetType());
    }

    private static void AssertNoProcessingAfterPeakLimiter(Project song)
    {
        var fx = song.Master!.Effects;
        var limAt = fx.ToList().FindIndex(e => e is PeakLimiterEffect);
        Assert.True(limAt >= 0, $"{song.Name}: master must include a Peak Limiter");
        for (var i = limAt + 1; i < fx.Count; i++)
        {
            var e = fx[i];
            Assert.True(e is SpectrumEffect or WaveformVisualizerEffect or ToolEffect { IsMeteringOnly: true },
                $"{song.Name}: {e.GetType().Name} must not follow Peak Limiter on Master");
        }
    }

    [Fact]
    public void FirstLight_UsesFullMaster()
    {
        var song = PreviewSongFactory.Create(Registry());
        AssertMasterMatchesRecipe(song, "full", typeof(WaveformVisualizerEffect));
        AssertNoProcessingAfterPeakLimiter(song);
    }

    [Fact]
    public void Ascension_UsesFullMaster_AndDrumBusCeilingAboveMaster()
    {
        var song = UpliftingTranceSongFactory.Create(Registry());
        AssertMasterMatchesRecipe(song, "full");
        AssertNoProcessingAfterPeakLimiter(song);

        var drums = song.Tracks.Single(t => t.Name == "Drums");
        var busLim = drums.Effects.OfType<LimiterEffect>().Single();
        var peak = song.Master!.Effects.OfType<PeakLimiterEffect>().Single();
        Assert.True(busLim.CeilingDb > peak.CeilingDb,
            "drum bus limiter must leave headroom for the master Peak Limiter");
        Assert.Contains(song.Tracks.Single(t => t.Name == "Leads").Effects, e => e is MultibandCompressorEffect);
        Assert.DoesNotContain(song.Master.Effects, e => e is MultibandCompressorEffect);
    }

    [Fact]
    public void Undertow_UsesClubLoudPlusAnalysers()
    {
        var song = DarkDnbSongFactory.Create(Registry());
        AssertMasterMatchesRecipe(song, "club", typeof(SpectrumEffect), typeof(WaveformVisualizerEffect));
        AssertNoProcessingAfterPeakLimiter(song);
    }

    [Fact]
    public void TrapBeat_UsesClubLoudPlusSpectrum()
    {
        var song = TrapBeatSongFactory.Create(Registry());
        AssertMasterMatchesRecipe(song, "club", typeof(SpectrumEffect));
        AssertNoProcessingAfterPeakLimiter(song);
    }

    [Fact]
    public void DustAndVinyl_UsesStreamingMaster()
    {
        var song = LoFiBeatSongFactory.Create(Registry());
        AssertMasterMatchesRecipe(song, "streaming");
        AssertNoProcessingAfterPeakLimiter(song);
    }

    [Fact]
    public void HouseStarter_UsesFullMaster()
    {
        var song = HouseStarterSongFactory.Create(Registry());
        AssertMasterMatchesRecipe(song, "full");
        AssertNoProcessingAfterPeakLimiter(song);
    }

    [Fact]
    public void TechnoStarter_UsesTechnoMaster()
    {
        var song = TechnoStarterSongFactory.Create(Registry());
        AssertMasterMatchesRecipe(song, "techno");
        AssertNoProcessingAfterPeakLimiter(song);
    }

    [Fact]
    public void FieldModular_UsesStreamingWithReverbBeforeLimiter()
    {
        var song = FieldModularSongFactory.Create(Registry());
        var fx = song.Master!.Effects.ToList();
        var limAt = fx.FindIndex(e => e is PeakLimiterEffect);
        var revAt = fx.FindIndex(e => e is ReverbEffect);
        Assert.True(revAt >= 0 && limAt > revAt);
        AssertNoProcessingAfterPeakLimiter(song);
        Assert.IsType<EqEffect>(fx[0]);
        Assert.IsType<CompressorEffect>(fx[1]);
        Assert.IsType<ReverbEffect>(fx[2]);
        Assert.IsType<PeakLimiterEffect>(fx[3]);
        Assert.IsType<SpectrumEffect>(fx[4]);
    }

    [Fact]
    public void StaticBloom_UsesStreamingWithReverbBeforeLimiter()
    {
        var song = StaticBloomSongFactory.Create(Registry());
        AssertNoProcessingAfterPeakLimiter(song);
        var fx = song.Master!.Effects;
        Assert.Contains(fx, e => e is ReverbEffect);
        var limAt = fx.ToList().FindIndex(e => e is PeakLimiterEffect);
        var revAt = fx.ToList().FindIndex(e => e is ReverbEffect);
        Assert.True(limAt > revAt);
    }

    [Fact]
    public void WebDemo_UsesLightweightStreamingMaster()
    {
        var song = WebDemoSongFactory.Create(Registry());
        var master = song.Master!.Effects;
        Assert.Equal(3, master.Count);
        Assert.IsType<EqEffect>(master[0]);
        Assert.IsType<CompressorEffect>(master[1]);
        Assert.IsType<PeakLimiterEffect>(master[2]);
        Assert.DoesNotContain(master, e => e is SpectrumEffect);
        AssertNoProcessingAfterPeakLimiter(song);
    }
}
