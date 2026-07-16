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

namespace Ongenet.Core.Persistence.Import.Ableton;

/// <summary>Conversion-only Ableton Live <c>.als</c> importer (BCL gzip + XML).</summary>
public sealed class AlsImporter : IProjectImporter
{
    private readonly ImportMapper _mapper;

    public AlsImporter(IInstrumentRegistry instruments, IEffectRegistry effects, IAudioFileService? audioFiles = null)
    {
        _mapper = new ImportMapper(instruments, effects, audioFiles);
    }

    public string FormatId => "als";

    public bool CanImport(string path) =>
        path.EndsWith(".als", StringComparison.OrdinalIgnoreCase);

    public ImportResult Import(string path)
    {
        using var fs = File.OpenRead(path);
        using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var reader = XmlReader.Create(gzip, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var root = XDocument.Load(reader).Root
            ?? throw new InvalidDataException("Empty Ableton Live set.");

        var doc = BuildDocument(path, root);
        return _mapper.Map(doc, FormatId, path);
    }

    internal static ImportDocument BuildDocument(string path, XElement root)
    {
        var liveSet = root.Name.LocalName == "Ableton"
            ? root.Element("LiveSet") ?? root
            : root;

        var projectDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var doc = new ImportDocument
        {
            Name = Path.GetFileNameWithoutExtension(path)
        };

        var tempo = liveSet.Descendants("Tempo").Elements("Manual").FirstOrDefault();
        if (tempo is not null && TryDouble(Attr(tempo, "Value"), out var bpm))
            doc.TempoBpm = bpm;

        var num = liveSet.Descendants("TimeSignature").Descendants("Numerator").FirstOrDefault();
        var den = liveSet.Descendants("TimeSignature").Descendants("Denominator").FirstOrDefault();
        if (num is not null && int.TryParse(Attr(num, "Value"), out var n))
            doc.TimeSigNumerator = n;
        if (den is not null && int.TryParse(Attr(den, "Value"), out var d))
            doc.TimeSigDenominator = d;

        doc.Tracks.Add(new ImportTrack
        {
            Id = "master",
            Name = "Master",
            Kind = ImportTrackKind.Master,
            Volume = 1.0
        });

        var tracksParent = liveSet.Element("Tracks") ?? liveSet;
        foreach (var trackEl in tracksParent.Elements())
        {
            var kind = trackEl.Name.LocalName switch
            {
                "AudioTrack" => ImportTrackKind.Audio,
                "MidiTrack" => ImportTrackKind.Instrument,
                "GroupTrack" => ImportTrackKind.Group,
                "ReturnTrack" => ImportTrackKind.Return,
                "MainTrack" or "MasterTrack" => ImportTrackKind.Master,
                _ => (ImportTrackKind?)null
            };
            if (kind is null) continue;

            var id = Attr(trackEl, "Id");
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");

            var name = FirstValue(trackEl, "EffectiveName", "UserName", "Name") ?? $"Track {id}";
            var track = new ImportTrack
            {
                Id = $"als:{id}",
                Name = name,
                Kind = kind.Value
            };

            var vol = trackEl.Descendants("Volume").Elements("Manual").FirstOrDefault();
            if (vol is not null && TryDouble(Attr(vol, "Value"), out var v))
                track.Volume = Math.Clamp(v, 0, 1);

            var pan = trackEl.Descendants("Pan").Elements("Manual").FirstOrDefault()
                      ?? trackEl.Descendants("TrackPan").Elements("Manual").FirstOrDefault();
            if (pan is not null && TryDouble(Attr(pan, "Value"), out var p))
                track.Pan = Math.Clamp(p * 2 - 1, -1, 1); // Live often 0..1

            if (kind == ImportTrackKind.Master)
            {
                // Merge devices onto existing master.
                var master = doc.Tracks.First(t => t.Kind == ImportTrackKind.Master);
                MapDeviceChain(trackEl, master, doc);
                continue;
            }

            MapDeviceChain(trackEl, track, doc);
            MapArrangementClips(trackEl, track, projectDir, doc);
            doc.Tracks.Add(track);
        }

        return doc;
    }

    private static void MapDeviceChain(XElement trackEl, ImportTrack track, ImportDocument doc)
    {
        foreach (var device in trackEl.Descendants("DeviceChain").Descendants()
                     .Where(e => e.Parent?.Name.LocalName is "Devices" or "DeviceChain"))
        {
            var local = device.Name.LocalName;
            if (local is "Devices" or "DeviceChain" or "Mixer" or "MainSequencer" or "FreezeSequencer")
                continue;

            // Leaf-ish device nodes typically have Id attribute.
            if (device.Attribute("Id") is null && !device.Elements().Any())
                continue;

            // Skip nested non-device containers.
            if (local is "Parameters" or "MidiCCOnOffThresholds" or "LomId")
                continue;

            var isInstrument = local is "OriginalSimpler" or "MultiSampler" or "InstrumentGroupDevice"
                or "PluginDevice" or "InstrumentVector" or "Drift" or "Meld" or "Wavetable"
                or "Operator" or "Analog" or "Collision" or "Electric" or "Tension" or "DrumRack"
                or "InstrumentRack";

            var thirdParty = local is "PluginDevice" or "AuPluginDevice" or "Vst3PluginDevice"
                or "MaxForLiveDevice" or "MxDeviceAudioEffect" or "MxDeviceInstrument";

            // Only treat direct children of Devices lists as devices when possible.
            if (device.Parent?.Name.LocalName is not "Devices" and not "DeviceChain")
            {
                if (!isInstrument && local is not ("Eq8" or "Compressor2" or "Reverb" or "Delay" or "Utility"
                        or "AutoFilter" or "GlueCompressor" or "Limiter" or "Gate" or "Saturator"
                        or "Overdrive" or "Chorus" or "Chorus2" or "Flanger" or "Phaser" or "PhaserNew"
                        or "Echo" or "Redux" or "Tuner" or "Spectrum" or "Amp" or "Cabinet" or "Pedal"
                        or "SimpleDelay" or "PingPongDelay" or "FilterDelay" or "MultibandDynamics"
                        or "Eq3" or "AutoPan" or "VinylDistortion" or "Vocoder" or "Corpus"
                        or "DynamicTube" or "Erosion" or "OriginalSimpler" or "MultiSampler"))
                    continue;
            }

            track.Devices.Add(new ImportDevice
            {
                Name = local,
                IsInstrument = isInstrument,
                IsThirdParty = thirdParty
            });

            if (thirdParty)
                doc.Warnings.Add($"Skipped third-party Ableton device '{local}' on '{track.Name}'.");
        }

        // Also pick Simpler sample path if present.
        var sampleRef = trackEl.Descendants("SampleRef").Elements("FileRef").FirstOrDefault();
        if (sampleRef is not null)
        {
            var samplePath = ResolveFileRef(sampleRef, Path.GetDirectoryName(track.Id) ?? ".");
            // projectDir not available here — path resolution done in MapArrangementClips mostly.
            _ = samplePath;
        }
    }

    private static void MapArrangementClips(XElement trackEl, ImportTrack track, string projectDir, ImportDocument doc)
    {
        foreach (var audioClip in trackEl.Descendants("AudioClip"))
        {
            var time = Attr(audioClip, "Time");
            TryDouble(time, out var startBeat);

            var loopEnd = audioClip.Descendants("LoopEnd").FirstOrDefault();
            var loopStart = audioClip.Descendants("LoopStart").FirstOrDefault();
            var length = 4.0;
            if (loopEnd is not null && TryDouble(Attr(loopEnd, "Value"), out var le) &&
                loopStart is not null && TryDouble(Attr(loopStart, "Value"), out var ls))
                length = Math.Max(0.25, le - ls);
            else
            {
                var currentEnd = audioClip.Descendants("CurrentEnd").FirstOrDefault();
                if (currentEnd is not null && TryDouble(Attr(currentEnd, "Value"), out var ce))
                    length = Math.Max(0.25, ce - startBeat);
            }

            var fileRef = audioClip.Descendants("SampleRef").Elements("FileRef").FirstOrDefault();
            var samplePath = fileRef is null ? null : ResolveFileRef(fileRef, projectDir);

            var clip = new ImportClip
            {
                Name = FirstValue(audioClip, "Name") ?? Path.GetFileNameWithoutExtension(samplePath ?? "Audio"),
                StartBeat = startBeat,
                LengthBeats = length,
                IsAudio = true,
                SamplePath = samplePath,
                StretchToTempo = true
            };

            foreach (var marker in audioClip.Descendants("WarpMarker"))
            {
                TryDouble(Attr(marker, "SecTime"), out var sec);
                TryDouble(Attr(marker, "BeatTime"), out var beat);
                clip.WarpMarkers.Add(new ImportWarpMarker { SourceSeconds = sec, BeatTime = beat });
            }

            if (samplePath is not null && !File.Exists(samplePath))
                doc.Warnings.Add($"Ableton sample missing: {samplePath}");

            track.Clips.Add(clip);
        }

        foreach (var midiClip in trackEl.Descendants("MidiClip"))
        {
            var time = Attr(midiClip, "Time");
            TryDouble(time, out var startBeat);
            var currentEnd = midiClip.Descendants("CurrentEnd").FirstOrDefault();
            var length = 4.0;
            if (currentEnd is not null && TryDouble(Attr(currentEnd, "Value"), out var ce))
                length = Math.Max(0.25, ce - startBeat);

            var clip = new ImportClip
            {
                Name = FirstValue(midiClip, "Name") ?? "MIDI",
                StartBeat = startBeat,
                LengthBeats = length,
                IsAudio = false
            };

            foreach (var keyTrack in midiClip.Descendants("KeyTrack"))
            {
                var midiKey = keyTrack.Element("MidiKey");
                var key = 60;
                if (midiKey is not null) int.TryParse(Attr(midiKey, "Value"), out key);

                foreach (var note in keyTrack.Descendants("MidiNoteEvent"))
                {
                    TryDouble(Attr(note, "Time"), out var t);
                    TryDouble(Attr(note, "Duration"), out var dur);
                    TryDouble(Attr(note, "Velocity"), out var vel);
                    clip.Notes.Add(new ImportNote
                    {
                        Key = key,
                        StartBeat = t,
                        LengthBeats = Math.Max(0.01, dur),
                        Velocity = (float)Math.Clamp(vel / 127.0, 0, 1)
                    });
                }
            }

            track.Clips.Add(clip);
            if (track.Kind == ImportTrackKind.Audio)
                track.Kind = ImportTrackKind.Instrument;
        }
    }

    private static string? ResolveFileRef(XElement fileRef, string projectDir)
    {
        var abs = fileRef.Element("Path") is { } p ? Attr(p, "Value") : null;
        if (!string.IsNullOrEmpty(abs) && File.Exists(abs)) return abs;

        var rel = fileRef.Element("RelativePath") is { } r ? Attr(r, "Value") : null;
        if (!string.IsNullOrEmpty(rel))
        {
            var combined = Path.GetFullPath(Path.Combine(projectDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(combined)) return combined;
            return combined;
        }

        return abs;
    }

    private static string Attr(XElement el, string name) =>
        el.Attribute(name)?.Value ?? el.Attribute(XName.Get(name))?.Value ?? "";

    private static string? FirstValue(XElement el, params string[] localNames)
    {
        foreach (var n in localNames)
        {
            var child = el.Descendants(n).FirstOrDefault() ?? el.Element(n);
            if (child is null) continue;
            var v = Attr(child, "Value");
            if (!string.IsNullOrWhiteSpace(v)) return v;
            if (!string.IsNullOrWhiteSpace(child.Value)) return child.Value;
        }
        return null;
    }

    private static bool TryDouble(string? s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
