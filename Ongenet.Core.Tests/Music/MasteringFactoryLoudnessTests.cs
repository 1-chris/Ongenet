using System;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Music;
using Ongenet.Core.Services;

namespace Ongenet.Core.Tests.Music;

/// <summary>
/// Offline loudness smoke for lightweight built-ins (normalize off). Documents that Peak Limiting
/// controls true peak while integrated LUFS typically sits below platform targets until export normalize.
/// </summary>
public class MasteringFactoryLoudnessTests
{
    private static readonly AudioFormat Format = new(48000, 2);

    private static IInstrumentRegistry Registry()
    {
        var instruments = new InstrumentRegistry();
        FieldBootstrap.Initialize(new FieldNodeRegistry(), instruments, new EffectRegistry());
        return instruments;
    }

    [Fact]
    public void WebDemo_OfflineBounce_TruePeakUnderStreamingCeiling()
    {
        var song = WebDemoSongFactory.Create(Registry());
        song.BarCount = 4;
        var buf = new OfflineRenderer().RenderMasterToBuffer(song, Format, WebDemoSongFactory.Bpm, null, 0, 4);
        var report = LoudnessAnalyzer.Analyze(buf.Samples, new AudioFormat(buf.SampleRate, buf.Channels),
            targetLufs: -14, targetTruePeakDbTp: -1);
        Assert.True(report.TruePeakDbTp <= -0.85f,
            $"Web Demo dBTP {report.TruePeakDbTp:F2} exceeds Streaming ceiling ({report.Summary})");
        Assert.False(float.IsNegativeInfinity(report.IntegratedLufs));
        // Without normalize, expect below Spotify −14 (document offset).
        Assert.True(report.IntegratedLufs < -10f);
    }

    [Fact]
    public void HouseStarter_OfflineBounce_TruePeakUnderStreamingCeiling()
    {
        var song = HouseStarterSongFactory.Create(Registry());
        song.BarCount = 4;
        var buf = new OfflineRenderer().RenderMasterToBuffer(song, Format, HouseStarterSongFactory.Bpm, null, 0, 4);
        var report = LoudnessAnalyzer.Analyze(buf.Samples, new AudioFormat(buf.SampleRate, buf.Channels),
            targetLufs: -14, targetTruePeakDbTp: -1);
        Assert.True(report.TruePeakDbTp <= -0.85f, report.Summary);
        Assert.False(float.IsNegativeInfinity(report.IntegratedLufs));
    }

    [Fact]
    public void TechnoStarter_OfflineBounce_TruePeakUnderMasterCeiling()
    {
        var song = TechnoStarterSongFactory.Create(Registry());
        song.BarCount = 4;
        var ceiling = song.Master!.Effects.OfType<PeakLimiterEffect>().Single().CeilingDb;
        var buf = new OfflineRenderer().RenderMasterToBuffer(song, Format, TechnoStarterSongFactory.Bpm, null, 0, 4);
        var report = LoudnessAnalyzer.Analyze(buf.Samples, new AudioFormat(buf.SampleRate, buf.Channels),
            targetTruePeakDbTp: ceiling);
        Assert.True(report.TruePeakDbTp <= ceiling + 0.2f, report.Summary);
    }
}
