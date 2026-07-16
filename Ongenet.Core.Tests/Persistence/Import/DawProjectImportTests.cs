using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Persistence.Import;
using Ongenet.Core.Persistence.Import.Ableton;
using Ongenet.Core.Persistence.Import.Bitwig;
using Ongenet.Core.Persistence.Import.DawProject;
using Ongenet.Core.Persistence.Import.FlStudio;

namespace Ongenet.Core.Tests.Persistence.Import;

public sealed class DawProjectImportTests
{
    [Fact]
    public void FlpImporter_reads_minimal_project_with_channel_and_tempo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-test-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpFixture.Minimal(tempo: 128, channelName: "Kick", sampleName: "kick.wav"));
            var importer = new FlpImporter(new InstrumentRegistry(), new EffectRegistry());
            Assert.True(importer.CanImport(path));

            var result = importer.Import(path);
            Assert.Equal("flp", result.SourceFormat);
            Assert.Equal(128, result.Project.Tempo.BeatsPerMinute, 3);
            Assert.Contains(result.Project.Tracks, t => t.Name == "Kick");
            Assert.Contains(result.UnresolvedSamplePaths, p => p.Contains("kick.wav", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void StockEffectMap_maps_fruity_and_ableton_devices()
    {
        Assert.True(StockEffectMap.TryMap("flp", "Fruity Reverb 2", out var flType));
        Assert.Equal("reverb", flType);
        Assert.True(StockEffectMap.TryMap("als", "Eq8", out var alsType));
        Assert.Equal("eq_plus", alsType);
        Assert.True(StockEffectMap.TryMap("dawproject", "compressor", out var dawType));
        Assert.Equal("compressor", dawType);
    }

    [Fact]
    public void AlsImporter_reads_gzipped_liveset()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-test-{Guid.NewGuid():N}.als");
        try
        {
            WriteGzip(path, AlsFixture.MinimalXml());
            var importer = new AlsImporter(new InstrumentRegistry(), new EffectRegistry());
            var result = importer.Import(path);
            Assert.Equal("als", result.SourceFormat);
            Assert.Equal(110, result.Project.Tempo.BeatsPerMinute, 3);
            Assert.Contains(result.Project.Tracks, t => t.Name == "Drums");
            Assert.Contains(result.Project.Tracks.SelectMany(t => t.Clips), c => c.IsAudio);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void DawprojectImporter_reads_zip_project_xml()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-test-{Guid.NewGuid():N}.dawproject");
        try
        {
            WriteDawproject(path, DawFixture.MinimalXml());
            var importer = new DawprojectImporter(new InstrumentRegistry(), new EffectRegistry());
            var result = importer.Import(path);
            Assert.Equal("dawproject", result.SourceFormat);
            Assert.Equal(100, result.Project.Tempo.BeatsPerMinute, 3);
            Assert.Contains(result.Project.Tracks, t => t.Name == "Audio 1");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void BwprojectImporter_extracts_embedded_sample_path_string()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ongenet-bw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var sample = Path.Combine(dir, "loop.wav");
        File.WriteAllBytes(sample, new byte[] { 0 }); // existence only; decode not required for path extract
        var path = Path.Combine(dir, "song.bwproject");
        try
        {
            // Binary blob containing the absolute sample path as UTF-8 text.
            var payload = Encoding.UTF8.GetBytes("meta\0" + sample + "\0trailer");
            File.WriteAllBytes(path, payload);

            var importer = new BwprojectImporter(new InstrumentRegistry(), new EffectRegistry());
            var result = importer.Import(path);
            Assert.Equal("bwproject", result.SourceFormat);
            Assert.Contains(result.Project.Tracks, t => t.Clips.Any(c =>
                c.AudioFilePath != null &&
                c.AudioFilePath.Contains("loop.wav", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains(result.Warnings, w => w.Contains("experimental", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(path);
            TryDelete(sample);
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ProjectImportService_dispatches_by_extension()
    {
        var flp = Path.Combine(Path.GetTempPath(), $"ongenet-test-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(flp, FlpFixture.Minimal(120, "Ch1", "x.wav"));
            var service = new ProjectImportService(new IProjectImporter[]
            {
                new FlpImporter(new InstrumentRegistry(), new EffectRegistry()),
                new AlsImporter(new InstrumentRegistry(), new EffectRegistry()),
                new DawprojectImporter(new InstrumentRegistry(), new EffectRegistry()),
                new BwprojectImporter(new InstrumentRegistry(), new EffectRegistry()),
            });
            Assert.True(service.CanImport(flp));
            var result = service.Import(flp);
            Assert.Equal("flp", result.SourceFormat);
        }
        finally
        {
            TryDelete(flp);
        }
    }

    private static void WriteGzip(string path, string xml)
    {
        using var fs = File.Create(path);
        using var gzip = new GZipStream(fs, CompressionLevel.Optimal);
        var bytes = Encoding.UTF8.GetBytes(xml);
        gzip.Write(bytes, 0, bytes.Length);
    }

    private static void WriteDawproject(string path, string projectXml)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("project.xml");
        using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
        w.Write(projectXml);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}

internal static class FlpFixture
{
    public static byte[] Minimal(int tempo, string channelName, string sampleName)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // FLhd
        w.Write(Encoding.ASCII.GetBytes("FLhd"));
        w.Write(6);
        w.Write((short)0);
        w.Write((ushort)1);
        w.Write((ushort)96);

        // Collect events first so FLdt size is exact.
        using var ev = new MemoryStream();
        using (var ew = new BinaryWriter(ev, Encoding.UTF8, leaveOpen: true))
        {
            WriteWord(ew, 66, (ushort)tempo); // Tempo
            WriteWord(ew, 64, 0); // NewChan 0
            WriteText(ew, 192, channelName); // ChanName
            WriteText(ew, 196, sampleName); // SampleFileName
            WriteText(ew, 201, "Sampler"); // GeneratorName
            WriteText(ew, 203, "Fruity Reverb 2"); // PluginName on channel → maps later as effect-ish
        }

        var events = ev.ToArray();
        w.Write(Encoding.ASCII.GetBytes("FLdt"));
        w.Write(events.Length);
        w.Write(events);
        return ms.ToArray();
    }

    private static void WriteWord(BinaryWriter w, byte id, ushort value)
    {
        w.Write(id);
        w.Write(value);
    }

    private static void WriteText(BinaryWriter w, byte id, string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        w.Write(id);
        WriteVarLen(w, bytes.Length);
        w.Write(bytes);
    }

    private static void WriteVarLen(BinaryWriter w, int length)
    {
        while (length > 0x7F)
        {
            w.Write((byte)((length & 0x7F) | 0x80));
            length >>= 7;
        }
        w.Write((byte)length);
    }
}

internal static class AlsFixture
{
    public static string MinimalXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Ableton MajorVersion="5" MinorVersion="12.0_12000">
          <LiveSet>
            <Tempo>
              <Manual Value="110" />
            </Tempo>
            <TimeSignature>
              <TimeSignatures>
                <RemoteableTimeSignature>
                  <Numerator Value="4" />
                  <Denominator Value="4" />
                </RemoteableTimeSignature>
              </TimeSignatures>
            </TimeSignature>
            <Tracks>
              <AudioTrack Id="8">
                <Name>
                  <EffectiveName Value="Drums" />
                  <UserName Value="Drums" />
                </Name>
                <DeviceChain>
                  <Mixer>
                    <Volume>
                      <Manual Value="0.85" />
                    </Volume>
                  </Mixer>
                  <MainSequencer>
                    <ClipTimeable>
                      <ArrangerAutomation>
                        <Events>
                          <AudioClip Id="0" Time="0">
                            <Name Value="Loop" />
                            <CurrentStart Value="0" />
                            <CurrentEnd Value="8" />
                            <Loop>
                              <LoopStart Value="0" />
                              <LoopEnd Value="8" />
                            </Loop>
                            <SampleRef>
                              <FileRef>
                                <RelativePath Value="Samples/loop.wav" />
                                <Path Value="" />
                              </FileRef>
                            </SampleRef>
                          </AudioClip>
                        </Events>
                      </ArrangerAutomation>
                    </ClipTimeable>
                  </MainSequencer>
                  <DeviceChain>
                    <Devices>
                      <Reverb Id="1" />
                      <Eq8 Id="2" />
                    </Devices>
                  </DeviceChain>
                </DeviceChain>
              </AudioTrack>
            </Tracks>
          </LiveSet>
        </Ableton>
        """;
}

internal static class DawFixture
{
    public static string MinimalXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Project version="1.0">
          <Transport Tempo="100">
            <TimeSignature Numerator="4" Denominator="4" />
          </Transport>
          <Structure>
            <Channel id="ch1" name="Audio 1" role="regular" contentType="audio" volume="0.9" pan="0" />
          </Structure>
          <Arrangement>
            <Lanes track="ch1">
              <Clips>
                <Clip time="0" duration="4" name="Clip A">
                  <Audio file="media/clip.wav" />
                </Clip>
              </Clips>
            </Lanes>
          </Arrangement>
        </Project>
        """;
}
