using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Persistence.Import.DawProject;

/// <summary>Conversion-only <c>.dawproject</c> importer (BCL ZIP + XML).</summary>
public sealed class DawprojectImporter : IProjectImporter
{
    private readonly ImportMapper _mapper;

    public DawprojectImporter(IInstrumentRegistry instruments, IEffectRegistry effects, IAudioFileService? audioFiles = null)
    {
        _mapper = new ImportMapper(instruments, effects, audioFiles);
    }

    public string FormatId => "dawproject";

    public bool CanImport(string path) =>
        path.EndsWith(".dawproject", StringComparison.OrdinalIgnoreCase);

    public ImportResult Import(string path)
    {
        using var fs = File.OpenRead(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        var entry = zip.GetEntry("project.xml")
            ?? throw new InvalidDataException("DAWproject archive missing project.xml.");

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var root = XDocument.Load(reader).Root
            ?? throw new InvalidDataException("Empty project.xml.");

        var doc = BuildDocument(path, root, zip);
        return _mapper.Map(doc, FormatId, path);
    }

    internal static ImportDocument BuildDocument(string path, XElement root, ZipArchive? zip)
    {
        var doc = new ImportDocument
        {
            Name = Path.GetFileNameWithoutExtension(path)
        };

        var transport = root.Element("Transport") ?? root.Descendants("Transport").FirstOrDefault();
        if (transport is not null)
        {
            if (TryDouble(Attr(transport, "Tempo"), out var bpm) ||
                TryDouble(Attr(transport.Element("Tempo"), "Value"), out bpm))
                doc.TempoBpm = bpm;

            var ts = transport.Element("TimeSignature") ?? transport.Descendants("TimeSignature").FirstOrDefault();
            if (ts is not null)
            {
                if (int.TryParse(Attr(ts, "Numerator"), out var n)) doc.TimeSigNumerator = n;
                if (int.TryParse(Attr(ts, "Denominator"), out var d)) doc.TimeSigDenominator = d;
            }
        }

        // Also accept Application/Transport metadata variants.
        var tempoEl = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "Tempo");
        if (doc.TempoBpm <= 0 && tempoEl is not null && TryDouble(Attr(tempoEl, "Value") is { Length: > 0 } v ? v : tempoEl.Value, out var t2))
            doc.TempoBpm = t2;

        doc.Tracks.Add(new ImportTrack
        {
            Id = "master",
            Name = "Master",
            Kind = ImportTrackKind.Master,
            Volume = 1.0
        });

        var structure = root.Element("Structure") ?? root;
        foreach (var channel in structure.Descendants().Where(e =>
                     e.Name.LocalName is "Channel" or "Track"))
        {
            // Prefer direct Structure children when available.
            if (channel.Parent is not null &&
                channel.Parent.Name.LocalName is not ("Structure" or "Track" or "Channel" or "Project"))
            {
                if (channel.Parent.Name.LocalName is "Lanes" or "Clips" or "Arrangement")
                    continue;
            }

            var id = Attr(channel, "id");
            if (string.IsNullOrEmpty(id)) id = Attr(channel, "Id");
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");

            var name = Attr(channel, "name");
            if (string.IsNullOrEmpty(name)) name = Attr(channel, "Name");
            if (string.IsNullOrEmpty(name)) name = $"Track {id}";

            var role = Attr(channel, "role");
            var kind = role.ToLowerInvariant() switch
            {
                "master" => ImportTrackKind.Master,
                "effect" or "return" => ImportTrackKind.Return,
                _ => ImportTrackKind.Audio
            };

            // ContentType hint.
            var content = Attr(channel, "contentType");
            if (content.Contains("notes", StringComparison.OrdinalIgnoreCase))
                kind = ImportTrackKind.Instrument;
            if (content.Contains("audio", StringComparison.OrdinalIgnoreCase) && kind == ImportTrackKind.Instrument)
                kind = ImportTrackKind.Instrument; // keep instrument; hybrid handled via clips

            if (kind == ImportTrackKind.Master)
            {
                MapDevices(channel, doc.Tracks[0], doc);
                continue;
            }

            var track = new ImportTrack
            {
                Id = id,
                Name = name,
                Kind = kind
            };

            if (TryDouble(Attr(channel, "volume"), out var vol))
                track.Volume = Math.Clamp(vol, 0, 1);
            if (TryDouble(Attr(channel, "pan"), out var pan))
                track.Pan = Math.Clamp(pan, -1, 1);

            MapDevices(channel, track, doc);
            doc.Tracks.Add(track);
        }

        // Arrangement lanes / clips
        var arrangement = root.Element("Arrangement") ?? root.Descendants("Arrangement").FirstOrDefault();
        if (arrangement is not null)
            MapLanes(arrangement, doc, path, zip);

        // Also scan top-level Lanes
        foreach (var lanes in root.Descendants("Lanes"))
            MapLanes(lanes, doc, path, zip);

        return doc;
    }

    private static void MapLanes(XElement parent, ImportDocument doc, string projectPath, ZipArchive? zip)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? ".";

        foreach (var lanes in parent.DescendantsAndSelf().Where(e => e.Name.LocalName == "Lanes"))
        {
            var trackId = Attr(lanes, "track");
            if (string.IsNullOrEmpty(trackId)) trackId = Attr(lanes, "Track");
            var track = doc.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track is null && !string.IsNullOrEmpty(trackId))
            {
                track = new ImportTrack { Id = trackId, Name = trackId, Kind = ImportTrackKind.Audio };
                doc.Tracks.Add(track);
            }
            track ??= doc.Tracks.LastOrDefault(t => t.Kind != ImportTrackKind.Master);
            if (track is null) continue;

            foreach (var clip in lanes.Descendants("Clip"))
            {
                TryDouble(Attr(clip, "time"), out var start);
                TryDouble(Attr(clip, "duration"), out var dur);
                if (dur <= 0) dur = 4;

                var name = Attr(clip, "name");
                if (string.IsNullOrEmpty(name)) name = "Clip";

                var audio = clip.Descendants("Audio").FirstOrDefault()
                            ?? clip.Descendants("WarpedAudio").FirstOrDefault();
                var notes = clip.Descendants("Notes").FirstOrDefault();

                if (audio is not null)
                {
                    var file = Attr(audio, "file");
                    if (string.IsNullOrEmpty(file))
                        file = Attr(audio.Element("File"), "path");
                    var samplePath = ResolveMedia(file, projectDir, zip);
                    track.Clips.Add(new ImportClip
                    {
                        Name = name,
                        StartBeat = start,
                        LengthBeats = dur,
                        IsAudio = true,
                        SamplePath = samplePath,
                        StretchToTempo = true
                    });
                    if (samplePath is not null && !File.Exists(samplePath) && zip?.GetEntry(file) is null)
                        doc.Warnings.Add($"DAWproject sample missing: {file}");
                }
                else if (notes is not null)
                {
                    var importClip = new ImportClip
                    {
                        Name = name,
                        StartBeat = start,
                        LengthBeats = dur,
                        IsAudio = false
                    };
                    foreach (var note in notes.Descendants("Note"))
                    {
                        int.TryParse(Attr(note, "key"), out var key);
                        TryDouble(Attr(note, "time"), out var t);
                        TryDouble(Attr(note, "duration"), out var nd);
                        TryDouble(Attr(note, "velocity"), out var vel);
                        importClip.Notes.Add(new ImportNote
                        {
                            Key = key,
                            StartBeat = t,
                            LengthBeats = Math.Max(0.01, nd),
                            Velocity = vel > 1 ? (float)Math.Clamp(vel / 127.0, 0, 1) : (float)Math.Clamp(vel, 0, 1)
                        });
                    }
                    track.Clips.Add(importClip);
                    if (track.Kind == ImportTrackKind.Audio)
                        track.Kind = ImportTrackKind.Instrument;
                }
            }
        }
    }

    private static void MapDevices(XElement channel, ImportTrack track, ImportDocument doc)
    {
        foreach (var device in channel.Descendants().Where(e =>
                     e.Name.LocalName is "Equalizer" or "Compressor" or "NoiseGate" or "Limiter"
                         or "Plugin" or "BuiltinDevice" or "Device"))
        {
            var name = device.Name.LocalName;
            if (name is "Plugin" or "Device" or "BuiltinDevice")
            {
                var deviceName = Attr(device, "name");
                if (string.IsNullOrEmpty(deviceName)) deviceName = Attr(device, "deviceName");
                if (string.IsNullOrEmpty(deviceName)) deviceName = Attr(device, "id");
                var plugin = name == "Plugin" ||
                             Attr(device, "pluginVersion").Length > 0 ||
                             Attr(device, "pluginName").Length > 0;
                track.Devices.Add(new ImportDevice
                {
                    Name = string.IsNullOrEmpty(deviceName) ? name : deviceName,
                    IsThirdParty = plugin
                });
                if (plugin)
                    doc.Warnings.Add($"Skipped third-party DAWproject plugin '{deviceName}' on '{track.Name}'.");
                continue;
            }

            // Generic built-ins in the DAWproject schema.
            var mappedName = name switch
            {
                "Equalizer" => "equalizer",
                "Compressor" => "compressor",
                "NoiseGate" => "noiseGate",
                "Limiter" => "limiter",
                _ => name
            };
            track.Devices.Add(new ImportDevice { Name = mappedName });
        }
    }

    private static string? ResolveMedia(string? file, string projectDir, ZipArchive? zip)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        if (Path.IsPathRooted(file) && File.Exists(file)) return file;
        var combined = Path.GetFullPath(Path.Combine(projectDir, file));
        if (File.Exists(combined)) return combined;

        // Embedded in ZIP — extract to temp beside project for playback.
        if (zip?.GetEntry(file) is { } entry)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ongenet-dawproject");
            Directory.CreateDirectory(tempDir);
            var dest = Path.Combine(tempDir, Path.GetFileName(file));
            if (!File.Exists(dest))
            {
                using var src = entry.Open();
                using var dst = File.Create(dest);
                src.CopyTo(dst);
            }
            return dest;
        }

        return file;
    }

    private static string Attr(XElement? el, string name)
    {
        if (el is null) return "";
        return el.Attribute(name)?.Value
               ?? el.Attribute(XName.Get(name, el.Name.NamespaceName))?.Value
               ?? "";
    }

    private static bool TryDouble(string? s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
