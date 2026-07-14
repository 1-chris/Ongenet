using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ongenet.Core.Audio.Field;

namespace Ongenet.Core.Persistence;

/// <summary>
/// Reads/writes a <c>.ongenfielddef</c> package: ZIP with a manifest, definition metadata + surface,
/// the Field graph document, and any embedded samples referenced by graph nodes.
/// </summary>
public static class FieldDefinitionFile
{
    public const int FormatVersion = 1;
    public const string Extension = ".ongenfielddef";

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ONGENFDF");
    private const string ManifestEntry = "definition.manifest";
    private const string MetaEntry = "definition.meta";
    private const string GraphEntry = "graph.dat";

    public sealed class LoadResult
    {
        public required FieldGraphDefinition Definition { get; init; }
        public required FieldGraph Graph { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    public static void Save(FieldGraphDefinition definition, FieldGraph graph, string author, Stream output)
    {
        definition.Author = string.IsNullOrWhiteSpace(author) ? definition.Author : author;
        definition.ModifiedTicks = DateTime.UtcNow.Ticks;

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        WriteEntry(zip, ManifestEntry, s =>
        {
            using var bw = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
            bw.Write(Magic);
            bw.Write(FormatVersion);
            bw.Write((int)definition.Role);
            bw.Write(definition.DefinitionId.ToByteArray());
            bw.Write(definition.TypeId ?? "");
            bw.Write(definition.DisplayName ?? "");
            bw.Write(definition.Category ?? "");
            bw.Write(definition.Author ?? "");
            bw.Write(definition.CreatedTicks);
            bw.Write(definition.ModifiedTicks);
        });

        WriteEntry(zip, MetaEntry, s =>
        {
            using var w = new OngenWriter(s);
            FieldSurfaceSerializer.Write(w, definition.Surface);
        });

        // Graph bytes via FieldGraphSerializer; samples stay inline inside that document.
        WriteEntry(zip, GraphEntry, s =>
        {
            using var w = new OngenWriter(s);
            FieldGraphSerializer.Write(w, graph);
        });
    }

    /// <summary>Reads only library listing metadata without decoding the graph.</summary>
    public static FieldGraphDefinition? ReadMeta(Stream input)
    {
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        return ReadMeta(zip);
    }

    public static LoadResult? Load(Stream input, IFieldNodeRegistry registry)
    {
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var meta = ReadMeta(zip);
        if (meta is null) return null;

        var metaEntry = zip.GetEntry(MetaEntry);
        var graphEntry = zip.GetEntry(GraphEntry);
        if (metaEntry is null || graphEntry is null) return null;

        using (var ms = ReadEntry(metaEntry))
        using (var r = new OngenReader(ms))
            meta.Surface = FieldSurfaceSerializer.Read(r);

        var graph = new FieldGraph();
        var warnings = new List<string>();
        using (var ms = ReadEntry(graphEntry))
        using (var r = new OngenReader(ms))
            FieldGraphSerializer.Read(r, graph, registry);

        // Soft validation: note missing widget targets.
        foreach (var exposed in meta.Surface.ExposedControls)
        {
            var binding = new FieldParameterBinding
            {
                NodeId = exposed.NodeId,
                ParamIndex = exposed.ParamIndex,
                ExpectedKind = exposed.ExpectedKind
            };
            if (!FieldExposedParameters.TryResolve(graph, binding, out _))
                warnings.Add($"Exposed control '{exposed.DisplayName}' could not be resolved.");
        }

        return new LoadResult { Definition = meta, Graph = graph, Warnings = warnings };
    }

    private static FieldGraphDefinition? ReadMeta(ZipArchive zip)
    {
        var manifest = zip.GetEntry(ManifestEntry);
        if (manifest is null) return null;
        using var ms = ReadEntry(manifest);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var magic = br.ReadBytes(Magic.Length);
        if (!MagicMatches(magic)) return null;
        _ = br.ReadInt32();
        var role = (FieldGraphRole)br.ReadInt32();
        var idBytes = br.ReadBytes(16);
        var typeId = br.ReadString();
        var displayName = br.ReadString();
        var category = br.ReadString();
        var author = br.ReadString();
        var created = br.ReadInt64();
        var modified = br.ReadInt64();
        _ = typeId; // derived from id + role; kept for debugging
        return new FieldGraphDefinition
        {
            DefinitionId = new Guid(idBytes),
            Role = role,
            DisplayName = displayName,
            Category = category,
            Author = author,
            CreatedTicks = created,
            ModifiedTicks = modified
        };
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
