using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Persistence;

/// <summary>Whether a preset stores an instrument, a single effect, or a whole effect chain.</summary>
public enum PresetKind
{
    Instrument,
    Effect,
    EffectChain
}

/// <summary>Metadata describing a preset (read from its manifest without decoding the component).</summary>
public sealed record PresetMeta(PresetKind Kind, string TypeId, string DisplayName, string Author, long CreatedTicks);

/// <summary>The result of loading a preset: its metadata plus the rebuilt instrument or effect.</summary>
public sealed class PresetLoadResult
{
    public required PresetMeta Meta { get; init; }
    public IInstrument? Instrument { get; init; }
    public IAudioEffect? Effect { get; init; }

    /// <summary>The effects of an <see cref="PresetKind.EffectChain"/> preset (in chain order); else null.</summary>
    public IReadOnlyList<IAudioEffect>? Effects { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = new List<string>();
}

/// <summary>
/// Reads and writes a single-file <c>.ongenpreset</c>: a ZIP holding a manifest, one component document
/// (an instrument or effect serialized with <see cref="ComponentSerializer"/>) and one de-duplicated
/// float32 WAV per embedded sample. Same shape as the <c>.ongen</c> project format, so a preset that hosts
/// audio (Basic Sampler / Granular / Padda custom waveform) is fully self-contained.
/// </summary>
public static class PresetFile
{
    public const int FormatVersion = 1;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ONGENPST"); // 8 bytes
    private const string ManifestEntry = "preset.manifest";
    private const string DataEntry = "preset.dat";

    public static void SaveInstrument(IInstrument instrument, string displayName, string author, Stream output)
        => Save(PresetKind.Instrument, instrument.TypeId, displayName, author, output,
            (w, store) => ComponentSerializer.WriteComponent(w, instrument.TypeId, instrument,
                instrument.Parameters, store, enabled: true, instrument as ISampleHost));

    public static void SaveEffect(IAudioEffect effect, string displayName, string author, Stream output)
        => Save(PresetKind.Effect, effect.TypeId, displayName, author, output,
            (w, store) => ComponentSerializer.WriteComponent(w, effect.TypeId, effect,
                effect.Parameters, store, effect.Enabled, host: null));

    /// <summary>Saves a whole effect chain (in order) as one preset.</summary>
    public static void SaveChain(IReadOnlyList<IAudioEffect> effects, string displayName, string author, Stream output)
        => Save(PresetKind.EffectChain, "chain", displayName, author, output, (w, store) =>
        {
            w.WriteInt(effects.Count);
            foreach (var fx in effects)
                ComponentSerializer.WriteComponent(w, fx.TypeId, fx, fx.Parameters, store, fx.Enabled, host: null);
        });

    private static void Save(PresetKind kind, string typeId, string displayName, string author,
        Stream output, Action<OngenWriter, SampleStore> writeComponent)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        var store = new SampleStore();
        using var doc = new MemoryStream();
        using (var w = new OngenWriter(doc)) writeComponent(w, store);

        WriteEntry(zip, ManifestEntry, s =>
        {
            using var bw = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
            bw.Write(Magic);
            bw.Write(FormatVersion);
            bw.Write((int)kind);
            bw.Write(typeId ?? "");
            bw.Write(displayName ?? "");
            bw.Write(author ?? "");
            bw.Write(DateTime.UtcNow.Ticks);
        });

        WriteEntry(zip, DataEntry, s => doc.WriteTo(s));

        foreach (var (hash, buffer) in store.Entries)
            WriteEntry(zip, $"samples/{hash}.wav", s => WavStream.WriteFloat32(s, buffer), CompressionLevel.Fastest);
    }

    /// <summary>Reads only a preset's manifest metadata (cheap — for listing a library without decoding).</summary>
    public static PresetMeta? ReadMeta(Stream input)
    {
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        return ReadMeta(zip);
    }

    public static PresetLoadResult? Load(Stream input, IInstrumentRegistry instruments, IEffectRegistry effects)
    {
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var meta = ReadMeta(zip);
        if (meta is null) return null;

        var dataEntry = zip.GetEntry(DataEntry);
        if (dataEntry is null) return null;

        var warnings = new List<string>();
        var sampleCache = new Dictionary<string, AudioSampleBuffer?>();

        AudioSampleBuffer? Lookup(string hash)
        {
            if (sampleCache.TryGetValue(hash, out var cached)) return cached;
            AudioSampleBuffer? buffer = null;
            var entry = zip.GetEntry($"samples/{hash}.wav");
            if (entry is not null)
            {
                try { using var ms = ReadEntry(entry); buffer = WavParser.Parse(ms); }
                catch { warnings.Add("A preset's embedded sample could not be read."); }
            }

            sampleCache[hash] = buffer;
            return buffer;
        }

        using var data = ReadEntry(dataEntry);
        using var r = new OngenReader(data);

        if (meta.Kind == PresetKind.Instrument)
        {
            var (inst, _) = ComponentSerializer.ReadInstrument(r, instruments, Lookup, warnings);
            return new PresetLoadResult { Meta = meta, Instrument = inst, Warnings = warnings };
        }

        if (meta.Kind == PresetKind.EffectChain)
        {
            var count = r.ReadInt();
            var chain = new List<IAudioEffect>(count);
            for (var i = 0; i < count; i++)
                if (ComponentSerializer.ReadEffect(r, effects, warnings) is { } e) chain.Add(e);
            return new PresetLoadResult { Meta = meta, Effects = chain, Warnings = warnings };
        }

        var fx = ComponentSerializer.ReadEffect(r, effects, warnings);
        return new PresetLoadResult { Meta = meta, Effect = fx, Warnings = warnings };
    }

    private static PresetMeta? ReadMeta(ZipArchive zip)
    {
        var manifest = zip.GetEntry(ManifestEntry);
        if (manifest is null) return null;

        using var ms = ReadEntry(manifest);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var magic = br.ReadBytes(Magic.Length);
        if (!MagicMatches(magic)) return null;
        _ = br.ReadInt32(); // format version (only v1 today)
        var kind = (PresetKind)br.ReadInt32();
        var typeId = br.ReadString();
        var displayName = br.ReadString();
        var author = br.ReadString();
        var ticks = br.ReadInt64();
        return new PresetMeta(kind, typeId, displayName, author, ticks);
    }

    private static void WriteEntry(ZipArchive zip, string name, Action<Stream> body,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var s = entry.Open();
        body(s);
    }

    private static MemoryStream ReadEntry(ZipArchiveEntry entry)
    {
        var ms = new MemoryStream();
        using (var s = entry.Open()) s.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    private static bool MagicMatches(byte[] read)
    {
        if (read.Length != Magic.Length) return false;
        for (var i = 0; i < Magic.Length; i++)
            if (read[i] != Magic[i]) return false;
        return true;
    }
}
