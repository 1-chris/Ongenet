using System;
using System.IO;
using System.Linq;
using System.Text;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence.Import;
using Ongenet.Core.Persistence.Import.FlStudio;

namespace Ongenet.Core.Tests.Persistence.Import;

public sealed class FlpImporterTests
{
    [Fact]
    public void Imports_channels_notes_and_playlist_pattern_as_midi_clips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 128,
                versionMajor: 21,
                channelCount: 2,
                channelNames: new[] { "Kick", "Hat" },
                sampleNames: new[] { "kick.wav", "hat.wav" },
                patternNotes: new[]
                {
                    // chan0 C4, chan1 F#4
                    (chan: 0, pos: 0, len: 48, key: 60, vel: 100),
                    (chan: 1, pos: 48, len: 48, key: 66, vel: 90),
                },
                playlist: new[]
                {
                    FlpModernFixture.PatternPlaylistItem(start: 0, length: 384, patternId: 1, track: 1)
                },
                fruityFxOnChannel: "Fruity Reverb 2"));

            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry()).Import(path);

            Assert.Equal(128, result.Project.Tempo.BeatsPerMinute, 3);
            Assert.Contains(result.Project.Tracks, t => t.Name == "Kick");
            Assert.Contains(result.Project.Tracks, t => t.Name == "Hat");

            var kick = result.Project.Tracks.First(t => t.Name == "Kick");
            var hat = result.Project.Tracks.First(t => t.Name == "Hat");
            Assert.Equal(TrackKind.Instrument, kick.Kind);
            Assert.Contains(kick.Clips, c => !c.IsAudio && c.Notes.Any(n => n.Note == 60));
            Assert.Contains(hat.Clips, c => !c.IsAudio && c.Notes.Any(n => n.Note == 66));
            Assert.Contains(kick.Effects, e => e.TypeId == "reverb");
            Assert.True(result.UnresolvedSamplePaths.Count >= 1);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Playlist_60_byte_stride_parses_without_desync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp21-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 140,
                versionMajor: 21,
                channelCount: 1,
                channelNames: new[] { "Samp" },
                sampleNames: new[] { "s.wav" },
                patternNotes: new[] { (chan: 0, pos: 0, len: 96, key: 60, vel: 100) },
                playlist: new[]
                {
                    FlpModernFixture.PatternPlaylistItem(start: 0, length: 384, patternId: 1, track: 0, fl21: true),
                    FlpModernFixture.PatternPlaylistItem(start: 384, length: 384, patternId: 1, track: 0, fl21: true),
                }));

            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry()).Import(path);
            var samp = Assert.Single(result.Project.Tracks, t => t.Name == "Samp");
            Assert.True(samp.Clips.Count(c => !c.IsAudio) >= 2);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Precreates_channels_from_header_count_but_prunes_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp-hdr-{Guid.NewGuid():N}.flp");
        try
        {
            // Header says 3 channels; only channel 0 is named/sampled — empty slots are pruned.
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 100,
                versionMajor: 20,
                channelCount: 3,
                channelNames: new[] { "A" }, // only name channel 0
                sampleNames: new[] { "a.wav" },
                patternNotes: Array.Empty<(int, int, int, int, int)>(),
                playlist: Array.Empty<byte[]>(),
                onlySelectFirstChannel: true));

            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry()).Import(path);
            Assert.Contains(result.Project.Tracks, t => t.Name == "A");
            Assert.DoesNotContain(result.Project.Tracks, t => t.Name == "Channel 2");
            Assert.DoesNotContain(result.Project.Tracks, t => t.Name == "Channel 3");
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FineTempo_is_not_overwritten_by_coarse_tempo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp-tempo-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 126,
                versionMajor: 21,
                channelCount: 1,
                channelNames: new[] { "Kick" },
                sampleNames: new[] { "kick.wav" },
                patternNotes: Array.Empty<(int, int, int, int, int)>(),
                playlist: Array.Empty<byte[]>(),
                writeCoarseTempoAfterFine: true,
                coarseTempoOverride: 120));

            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry()).Import(path);
            Assert.Equal(126, result.Project.Tempo.BeatsPerMinute, 3);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Playlist_88_byte_stride_fl26_parses_repeated_pattern_clips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp88-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 126,
                versionMajor: 26,
                channelCount: 1,
                channelNames: new[] { "Hat" },
                sampleNames: new[] { "hat.wav" },
                patternNotes: new[] { (chan: 0, pos: 0, len: 48, key: 60, vel: 100) },
                playlist: new[]
                {
                    FlpModernFixture.PatternPlaylistItem(start: 0, length: 384, patternId: 1, track: 0, fl88: true),
                    FlpModernFixture.PatternPlaylistItem(start: 384, length: 384, patternId: 1, track: 0, fl88: true),
                    FlpModernFixture.PatternPlaylistItem(start: 768, length: 384, patternId: 1, track: 0, fl88: true),
                    FlpModernFixture.PatternPlaylistItem(start: 1152, length: 384, patternId: 1, track: 0, fl88: true),
                }));

            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry()).Import(path);
            var hat = Assert.Single(result.Project.Tracks, t => t.Name == "Hat");
            Assert.True(hat.Clips.Count(c => !c.IsAudio) >= 4);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Layer_children_receive_fanned_out_pattern_notes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp-layer-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 126,
                versionMajor: 26,
                channelCount: 3,
                channelNames: new[] { "KICK", "KickA", "KickB" },
                sampleNames: new[] { "", "a.wav", "b.wav" },
                patternNotes: new[] { (chan: 0, pos: 0, len: 48, key: 60, vel: 100) }, // notes on Layer
                playlist: new[]
                {
                    FlpModernFixture.PatternPlaylistItem(start: 0, length: 384, patternId: 1, track: 0, fl88: true),
                },
                chanTypes: new byte[] { 3, 0, 0 }, // Layer, Sampler, Sampler
                layerChildren: new Dictionary<int, int[]> { [0] = new[] { 1, 2 } }));

            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry()).Import(path);
            var kickA = Assert.Single(result.Project.Tracks, t => t.Name == "KickA");
            var kickB = Assert.Single(result.Project.Tracks, t => t.Name == "KickB");
            Assert.Contains(kickA.Clips, c => !c.IsAudio && c.Notes.Any(n => n.Note == 60));
            Assert.Contains(kickB.Clips, c => !c.IsAudio && c.Notes.Any(n => n.Note == 60));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Playlist_80_byte_stride_fl25_parses_pattern_clips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp25-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 126,
                versionMajor: 25,
                channelCount: 1,
                channelNames: new[] { "Kick" },
                sampleNames: new[] { "kick.wav" },
                patternNotes: new[] { (chan: 0, pos: 0, len: 96, key: 60, vel: 100) },
                playlist: new[]
                {
                    FlpModernFixture.PatternPlaylistItem(start: 0, length: 384, patternId: 1, track: 0, fl21: true, fl25: true),
                }));

            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry()).Import(path);
            var kick = Assert.Single(result.Project.Tracks, t => t.Name == "Kick");
            Assert.Contains(kick.Clips, c => !c.IsAudio && c.Notes.Any(n => n.Note == 60));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Fl26_three_byte_event_172_keeps_stream_in_sync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp26-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 126,
                versionMajor: 26,
                channelCount: 2,
                channelNames: new[] { "Kick", "Hat" },
                sampleNames: new[] { "kick.wav", "hat.wav" },
                patternNotes: new[]
                {
                    (chan: 0, pos: 0, len: 48, key: 60, vel: 100),
                    (chan: 1, pos: 48, len: 48, key: 66, vel: 90),
                },
                playlist: new[]
                {
                    FlpModernFixture.PatternPlaylistItem(start: 0, length: 384, patternId: 1, track: 0, fl21: true, fl25: true),
                },
                insertFl26Event172: true));

            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry()).Import(path);
            Assert.Equal(126, result.Project.Tempo.BeatsPerMinute, 3);
            Assert.Contains(result.Project.Tracks, t => t.Name == "Kick");
            Assert.Contains(result.Project.Tracks, t => t.Name == "Hat");
            var kick = result.Project.Tracks.First(t => t.Name == "Kick");
            Assert.Contains(kick.Clips, c => !c.IsAudio && c.Notes.Any());
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Levels_24byte_uses_integer_fl_scale_not_denormal_floats()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp-levels-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 126,
                versionMajor: 26,
                channelCount: 1,
                channelNames: new[] { "Kick" },
                sampleNames: new[] { "kick.wav" },
                patternNotes: Array.Empty<(int, int, int, int, int)>(),
                playlist: Array.Empty<byte[]>(),
                levelsPan: 6400,
                levelsVolume: 10000));

            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry()).Import(path);
            var kick = Assert.Single(result.Project.Tracks, t => t.Name == "Kick");
            Assert.InRange(kick.Volume, 0.75, 0.82); // 10000/12800
            Assert.InRange(kick.Pan, -0.05, 0.05); // center 6400
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Native_channel_with_3x_osc_maps_to_triple_osc_instrument()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp-3xosc-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 126,
                versionMajor: 26,
                channelCount: 1,
                channelNames: new[] { "Lead" },
                sampleNames: Array.Empty<string>(),
                patternNotes: new[] { (chan: 0, pos: 0, len: 96, key: 60, vel: 100) },
                playlist: new[]
                {
                    FlpModernFixture.PatternPlaylistItem(start: 0, length: 384, patternId: 1, track: 0, fl21: true, fl25: true),
                },
                chanType: 2, // Native
                generatorName: "3x Osc"));

            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry()).Import(path);
            var lead = Assert.Single(result.Project.Tracks, t => t.Name == "Lead");
            Assert.Equal(TrackKind.Instrument, lead.Kind);
            Assert.Equal(TripleOscInstrument.TypeId, Assert.Single(lead.Instruments).Instrument.TypeId);
            Assert.Contains(lead.Clips, c => !c.IsAudio && c.Notes.Any(n => n.Note == 60));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Factory_placeholder_expands_when_fl_studio_app_bundle_exists()
    {
        var roots = Directory.Exists("/Applications")
            ? Directory.GetDirectories("/Applications", "FL Studio*.app")
            : Array.Empty<string>();
        if (roots.Length == 0)
            return; // no FL install in CI

        var fl = Path.Combine(roots.OrderByDescending(r => r).First(), "Contents", "Resources", "FL");
        if (!Directory.Exists(Path.Combine(fl, "Data")))
            return;

        var path = Path.Combine(Path.GetTempPath(), $"ongenet-flp-factory-{Guid.NewGuid():N}.flp");
        try
        {
            File.WriteAllBytes(path, FlpModernFixture.Build(
                tempo: 126,
                versionMajor: 26,
                channelCount: 1,
                channelNames: new[] { "Kick" },
                sampleNames: new[] { "%FLStudioFactoryData%/Data/Patches/Packs/Drums/Kicks/Grv Kick 18.wav" },
                patternNotes: Array.Empty<(int, int, int, int, int)>(),
                playlist: Array.Empty<byte[]>()));

            var audio = new Ongenet.Core.Audio.Files.AudioFileService(
                new Ongenet.Core.Audio.Files.IAudioFileDecoder[]
                {
                    new Ongenet.Core.Audio.Files.WavFileDecoder(),
                    new Ongenet.Core.Audio.Files.FfmpegAudioDecoder()
                });
            var result = new FlpImporter(new InstrumentRegistry(), new EffectRegistry(), audio).Import(path);
            Assert.Empty(result.UnresolvedSamplePaths);
            ImportAudioHydrator.Hydrate(result.Project, audio);
            var kick = Assert.Single(result.Project.Tracks, t => t.Name == "Kick" || t.Name == "Grv Kick 18");
            var host = Assert.IsAssignableFrom<ISampleHost>(Assert.Single(kick.Instruments).Instrument);
            Assert.False(string.IsNullOrEmpty(host.SampleName));
            Assert.NotNull(host.CurrentSample);
            Assert.True(host.CurrentSample!.Samples.Length > 0);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}

/// <summary>Builds minimal modern-shaped FLP binaries for importer tests.</summary>
internal static class FlpModernFixture
{
    public const ushort Ppq = 96;
    public const ushort PatternBase = 20480;

    public static byte[] PatternPlaylistItem(
        int start, int length, int patternId, int track, bool fl21 = false, bool fl25 = false, bool fl88 = false)
    {
        var stride = fl88 ? 88 : fl25 ? 80 : fl21 ? 60 : 32;
        var buf = new byte[stride];
        BitConverter.TryWriteBytes(buf.AsSpan(0), start);
        BitConverter.TryWriteBytes(buf.AsSpan(4), PatternBase);
        BitConverter.TryWriteBytes(buf.AsSpan(6), (ushort)(PatternBase + patternId));
        BitConverter.TryWriteBytes(buf.AsSpan(8), length);
        if (fl21 || fl25 || fl88)
        {
            var rvidx = (ushort)(499 - track);
            BitConverter.TryWriteBytes(buf.AsSpan(12), rvidx);
            BitConverter.TryWriteBytes(buf.AsSpan(18), (ushort)64); // flags
        }
        else
        {
            var raw = 198 - track;
            BitConverter.TryWriteBytes(buf.AsSpan(12), raw);
            BitConverter.TryWriteBytes(buf.AsSpan(18), (ushort)0);
        }
        return buf;
    }

    public static byte[] Build(
        int tempo,
        int versionMajor,
        int channelCount,
        string[] channelNames,
        string[] sampleNames,
        (int chan, int pos, int len, int key, int vel)[] patternNotes,
        byte[][] playlist,
        string? fruityFxOnChannel = null,
        bool onlySelectFirstChannel = false,
        bool writeCoarseTempoAfterFine = false,
        int? coarseTempoOverride = null,
        bool insertFl26Event172 = false,
        byte chanType = 0,
        string? generatorName = null,
        int? levelsPan = null,
        int? levelsVolume = null,
        byte[]? chanTypes = null,
        Dictionary<int, int[]>? layerChildren = null)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(Encoding.ASCII.GetBytes("FLhd"));
        w.Write(6);
        w.Write((short)0);
        w.Write((ushort)channelCount);
        w.Write(Ppq);

        using var ev = new MemoryStream();
        using (var ew = new BinaryWriter(ev, Encoding.UTF8, leaveOpen: true))
        {
            WriteTextUtf8(ew, 199, $"{versionMajor}.0.0"); // Version
            WriteDword(ew, 159, 1); // FLBuild (harmless)

            if (insertFl26Event172)
            {
                // FL26 quirk: event 172 has a 3-byte payload; product string follows as TEXT 192.
                ew.Write((byte)172);
                ew.Write(new byte[] { 0x01, 0x01, 0x00 });
                WriteText(ew, 192, $"FL Studio {versionMajor}.0.0");
            }

            WriteDword(ew, 156, (uint)(tempo * 1000)); // FineTempo
            if (writeCoarseTempoAfterFine)
                WriteWord(ew, 66, (ushort)(coarseTempoOverride ?? tempo));
            else
                WriteWord(ew, 66, (ushort)tempo);

            // One arrangement section (mirrors FL dumping NewArrangement before playlist).
            WriteWord(ew, 99, 0); // NewArrangement

            var channelsToEmit = onlySelectFirstChannel ? 1 : channelCount;
            for (var i = 0; i < channelsToEmit; i++)
            {
                WriteWord(ew, 64, (ushort)i); // NewChan
                var type = chanTypes is { Length: > 0 } && i < chanTypes.Length ? chanTypes[i] : chanType;
                WriteByte(ew, 21, type);
                if (i < channelNames.Length && !string.IsNullOrEmpty(channelNames[i]))
                    WriteText(ew, 192, channelNames[i]);
                if (i < sampleNames.Length && !string.IsNullOrEmpty(sampleNames[i]))
                    WriteText(ew, 196, sampleNames[i]);
                var gen = generatorName ?? (type == 3 ? "Sampler" : "Sampler");
                if (type != 3)
                    WriteText(ew, 201, gen);
                if (layerChildren is not null && layerChildren.TryGetValue(i, out var kids))
                {
                    foreach (var child in kids)
                        WriteWord(ew, 94, (ushort)child); // Children
                }
                if (levelsPan is int pan && levelsVolume is int vol)
                {
                    var levels = new byte[24];
                    BitConverter.TryWriteBytes(levels.AsSpan(0), pan);
                    BitConverter.TryWriteBytes(levels.AsSpan(4), vol);
                    WriteData(ew, 219, levels); // Levels
                }
                if (i == 0 && fruityFxOnChannel is not null)
                    WriteText(ew, 203, fruityFxOnChannel);
            }

            WriteWord(ew, 65, 1); // NewPat 1
            WriteText(ew, 193, "Pat 1");
            if (patternNotes.Length > 0)
                WriteData(ew, 224, BuildNotes(patternNotes));

            if (playlist.Length > 0)
            {
                var joined = playlist.SelectMany(b => b).ToArray();
                WriteData(ew, 233, joined);
            }
        }

        var events = ev.ToArray();
        w.Write(Encoding.ASCII.GetBytes("FLdt"));
        w.Write(events.Length);
        w.Write(events);
        return ms.ToArray();
    }

    private static byte[] BuildNotes((int chan, int pos, int len, int key, int vel)[] notes)
    {
        var buf = new byte[notes.Length * 24];
        for (var i = 0; i < notes.Length; i++)
        {
            var o = i * 24;
            var n = notes[i];
            BitConverter.TryWriteBytes(buf.AsSpan(o), n.pos);
            BitConverter.TryWriteBytes(buf.AsSpan(o + 4), (ushort)0); // flags
            BitConverter.TryWriteBytes(buf.AsSpan(o + 6), (ushort)n.chan);
            BitConverter.TryWriteBytes(buf.AsSpan(o + 8), n.len);
            BitConverter.TryWriteBytes(buf.AsSpan(o + 12), (ushort)n.key);
            buf[o + 21] = (byte)n.vel;
        }
        return buf;
    }

    private static void WriteByte(BinaryWriter w, byte id, byte value)
    {
        w.Write(id);
        w.Write(value);
    }

    private static void WriteWord(BinaryWriter w, byte id, ushort value)
    {
        w.Write(id);
        w.Write(value);
    }

    private static void WriteDword(BinaryWriter w, byte id, uint value)
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

    private static void WriteTextUtf8(BinaryWriter w, byte id, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        w.Write(id);
        WriteVarLen(w, bytes.Length);
        w.Write(bytes);
    }

    private static void WriteData(BinaryWriter w, byte id, byte[] data)
    {
        w.Write(id);
        WriteVarLen(w, data.Length);
        w.Write(data);
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
