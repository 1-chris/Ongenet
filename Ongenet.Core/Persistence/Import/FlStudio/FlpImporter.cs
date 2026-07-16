using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Persistence.Import.FlStudio;

/// <summary>Conversion-only FL Studio <c>.flp</c> importer (from-scratch TLV parser).</summary>
public sealed class FlpImporter : IProjectImporter
{
    private readonly ImportMapper _mapper;

    public FlpImporter(IInstrumentRegistry instruments, IEffectRegistry effects, IAudioFileService? audioFiles = null)
    {
        _mapper = new ImportMapper(instruments, effects, audioFiles);
    }

    public string FormatId => "flp";

    public bool CanImport(string path) =>
        path.EndsWith(".flp", StringComparison.OrdinalIgnoreCase);

    public ImportResult Import(string path)
    {
        using var fs = File.OpenRead(path);
        var (header, events) = FlpEventReader.Read(fs);
        var doc = BuildDocument(path, header, events);
        return _mapper.Map(doc, FormatId, path);
    }

    /// <summary>Build an <see cref="ImportDocument"/> from parsed events (also used by tests).</summary>
    internal static ImportDocument BuildDocument(string path, FlpEventReader.Header header, IReadOnlyList<FlpEvent> events)
    {
        var doc = new ImportDocument
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Ppq = header.Ppq > 0 ? header.Ppq : 96,
            TempoBpm = 120
        };

        var projectDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var channels = new Dictionary<int, ImportTrack>();
        var patterns = new Dictionary<int, ImportPattern>();
        var inserts = new Dictionary<int, ImportTrack>();
        var playlistTrackNames = new Dictionary<int, string>();
        var versionMajor = 0;
        var newChanCount = 0;
        var patternNotesCount = 0;
        var patternNotesDropped = 0;
        var playlistEventCount = 0;
        var playlistItemCount = 0;
        var hasFineTempo = false;
        var tempoCoarse = (double?)null;
        var tempoFineFrac = 0.0;
        var currentPatNum = 0;

        // Pre-create channel slots from header so NewChan is selection, not creation-only.
        var channelCount = Math.Clamp((int)header.ChannelCount, 0, 1000);
        for (var i = 0; i < channelCount; i++)
        {
            channels[i] = new ImportTrack
            {
                Id = $"chan:{i}",
                Name = $"Channel {i + 1}",
                Kind = ImportTrackKind.Instrument,
                Volume = 0.8
            };
        }

        // Do not default-select channel 0 — early TEXT events (e.g. product string as id 192)
        // would otherwise rename the first rack channel.
        ImportTrack? curChannel = null;
        ImportPattern? curPattern = null;
        ImportTrack? curInsert = null;
        var curInsertId = 0;
        var curPlaylistTrack = 0;

        inserts[0] = new ImportTrack
        {
            Id = "insert:0",
            Name = "Master",
            Kind = ImportTrackKind.Master,
            Volume = 1.0
        };
        curInsert = inserts[0];

        foreach (var ev in events)
        {
            switch (ev.Id)
            {
                case FlpEventId.Tempo:
                    tempoCoarse = FlpEventReader.ReadU16(ev.Data);
                    if (!hasFineTempo)
                        doc.TempoBpm = tempoCoarse.Value + tempoFineFrac;
                    break;
                case FlpEventId.TempoFine:
                    tempoFineFrac = FlpEventReader.ReadU16(ev.Data) / 1000.0;
                    if (!hasFineTempo && tempoCoarse is double coarse)
                        doc.TempoBpm = coarse + tempoFineFrac;
                    break;
                case FlpEventId.FineTempo:
                    doc.TempoBpm = FlpEventReader.ReadU32(ev.Data) / 1000.0;
                    hasFineTempo = true;
                    break;
                case FlpEventId.Title:
                    doc.Name = FlpEventReader.ReadUnicode(ev.Data);
                    break;
                case FlpEventId.Version:
                {
                    var ver = FlpEventReader.ReadUtf8(ev.Data);
                    doc.SourceVersion = ver;
                    var parts = ver.Split('.');
                    if (parts.Length > 0 && int.TryParse(parts[0], out var maj))
                        versionMajor = maj;
                    break;
                }
                case FlpEventId.NewChan:
                {
                    newChanCount++;
                    var id = FlpEventReader.ReadU16(ev.Data);
                    if (!channels.TryGetValue(id, out curChannel))
                    {
                        curChannel = new ImportTrack
                        {
                            Id = $"chan:{id}",
                            Name = $"Channel {id + 1}",
                            Kind = ImportTrackKind.Instrument,
                            Volume = 0.8
                        };
                        channels[id] = curChannel;
                    }
                    break;
                }
                case FlpEventId.ChanType:
                    if (curChannel is not null && ev.Data.Length > 0)
                        ApplyChanType(curChannel, ev.Data[0], doc);
                    break;
                case FlpEventId.ChanName:
                    if (curChannel is not null)
                        curChannel.Name = FlpEventReader.ReadUnicode(ev.Data);
                    break;
                case FlpEventId.Color:
                    if (curChannel is not null && ev.Data.Length >= 4)
                        curChannel.ColorHex = ColorToHex(FlpEventReader.ReadU32(ev.Data));
                    break;
                case FlpEventId.SampleFileName:
                    if (curChannel is not null)
                    {
                        var sample = FlpEventReader.ReadUnicode(ev.Data);
                        curChannel.SamplePath = ResolveSamplePath(sample, projectDir);
                        // Pure audio-clip channels use generator name AudioClip; keep those as Audio.
                        var hasSynth = curChannel.Devices.Any(d =>
                            d.IsInstrument &&
                            !d.Name.Equals("Sampler", StringComparison.OrdinalIgnoreCase) &&
                            !d.Name.Equals("AudioClip", StringComparison.OrdinalIgnoreCase));
                        if (!hasSynth &&
                            curChannel.Devices.Any(d =>
                                d.IsInstrument &&
                                d.Name.Equals("AudioClip", StringComparison.OrdinalIgnoreCase)))
                            curChannel.Kind = ImportTrackKind.Audio;
                        else if (curChannel.Kind != ImportTrackKind.Audio)
                            curChannel.Kind = ImportTrackKind.Instrument;
                        // Only attach a Sampler device when no generator is known yet.
                        if (curChannel.Devices.All(d => !d.IsInstrument))
                            curChannel.Devices.Add(new ImportDevice { Name = "Sampler", IsInstrument = true });
                        // Prefer a readable name from the sample when FL left the default Channel N.
                        if (IsDefaultChannelName(curChannel.Name) && !string.IsNullOrWhiteSpace(sample))
                            curChannel.Name = Path.GetFileNameWithoutExtension(sample.Replace('\\', '/'));
                    }
                    break;
                case FlpEventId.GeneratorName:
                    if (curChannel is not null)
                    {
                        var gen = FlpEventReader.ReadUnicode(ev.Data);
                        if (!string.IsNullOrWhiteSpace(gen) &&
                            !gen.Equals("Sampler", StringComparison.OrdinalIgnoreCase) &&
                            !gen.Equals("AudioClip", StringComparison.OrdinalIgnoreCase))
                        {
                            // FL also dumps insert FX internal names under this id in some versions.
                            var looksLikeFx = StockEffectMap.TryMap("flp", gen, out _) ||
                                              gen.StartsWith("Fruity", StringComparison.OrdinalIgnoreCase) ||
                                              gen.Equals("Soundgoodizer", StringComparison.OrdinalIgnoreCase) ||
                                              gen.Equals("Maximus", StringComparison.OrdinalIgnoreCase) ||
                                              gen.Equals("Gross Beat", StringComparison.OrdinalIgnoreCase) ||
                                              gen.Equals("Edison", StringComparison.OrdinalIgnoreCase) ||
                                              gen.Equals("Transient Processor", StringComparison.OrdinalIgnoreCase) ||
                                              gen.Equals("Wave Candy", StringComparison.OrdinalIgnoreCase) ||
                                              gen.Equals("Control Surface", StringComparison.OrdinalIgnoreCase);
                            if (looksLikeFx)
                            {
                                var target = curInsert ?? curChannel;
                                // Keep at most a few FX device hints per target to limit UI rebuild cost.
                                if (target.Devices.Count(d => !d.IsInstrument) < 6)
                                {
                                    target.Devices.Add(new ImportDevice
                                    {
                                        Name = gen,
                                        IsInstrument = false,
                                        IsThirdParty = !StockEffectMap.TryMap("flp", gen, out _)
                                    });
                                }
                            }
                            else
                            {
                                var thirdParty = IsLikelyThirdPartyGenerator(gen);
                                // Replace a prior placeholder Sampler device with the real generator.
                                curChannel.Devices.RemoveAll(d =>
                                    d.IsInstrument &&
                                    d.Name.Equals("Sampler", StringComparison.OrdinalIgnoreCase));
                                curChannel.Devices.Add(new ImportDevice
                                {
                                    Name = gen,
                                    IsInstrument = true,
                                    IsThirdParty = thirdParty
                                });
                                if (IsDefaultChannelName(curChannel.Name))
                                    curChannel.Name = gen;
                                if (thirdParty)
                                    doc.Warnings.Add($"Channel '{curChannel.Name}' uses third-party/plugin generator '{gen}'.");
                            }
                        }
                    }
                    break;
                case FlpEventId.PluginName:
                {
                    var plugin = FlpEventReader.ReadUnicode(ev.Data);
                    if (string.IsNullOrWhiteSpace(plugin)) break;

                    // FL26 dumps channel display names under PluginName (Wavvyy Synth, FUTURE Bass, …).
                    // Prefer those over technical generator names (3x Osc, GMS, …).
                    if (curChannel is not null && !LooksLikeRealPluginName(plugin))
                    {
                        if (IsDefaultChannelName(curChannel.Name) || IsGeneratorTypeName(curChannel.Name))
                            curChannel.Name = plugin;
                        break;
                    }

                    if (LooksLikeSampleOrDisplayName(plugin) || !LooksLikeRealPluginName(plugin))
                        break;

                    var target = curChannel ?? curInsert;
                    if (target is null) break;
                    var mapsAsFx = StockEffectMap.TryMap("flp", plugin, out _) ||
                                   plugin.StartsWith("Fruity", StringComparison.OrdinalIgnoreCase);
                    target.Devices.Add(new ImportDevice
                    {
                        Name = plugin,
                        IsInstrument = false,
                        IsThirdParty = !mapsAsFx
                    });
                    if (!mapsAsFx)
                        doc.Warnings.Add($"Skipped or flagged plugin '{plugin}'.");
                    break;
                }
                case FlpEventId.Levels: // also legacy BasicChanParams
                    if (curChannel is not null)
                        ApplyLevelsOrBasicParams(curChannel, ev.Data);
                    break;
                case FlpEventId.MixSliceNum:
                    if (curChannel is not null && ev.Data.Length > 0)
                    {
                        curChannel.MixerInsertIndex = ev.Data[0];
                        // Insert 0 is the master — leave ParentId null so audio routes to master normally.
                        if (ev.Data[0] > 0)
                            curChannel.ParentId = $"insert:{ev.Data[0]}";
                    }
                    break;
                case FlpEventId.Children:
                    // Layer → child rack channel (WORD). Emitted once per child while the Layer is selected.
                    if (curChannel is not null)
                    {
                        var child = FlpEventReader.ReadU16(ev.Data);
                        if (!curChannel.ChildChannelIds.Contains(child))
                            curChannel.ChildChannelIds.Add(child);
                    }
                    break;
                case FlpEventId.Cutoff:
                    if (curChannel is not null)
                        curChannel.FilterCutoff = FlpEventReader.ReadU16(ev.Data);
                    break;
                case FlpEventId.Resonance:
                    if (curChannel is not null)
                        curChannel.FilterResonance = FlpEventReader.ReadU16(ev.Data);
                    break;
                case FlpEventId.ChanParams:
                    if (curChannel is not null)
                        ApplyChanParams(curChannel, ev.Data);
                    break;
                case FlpEventId.NewArrangement:
                    // FL stores one playlist blob per arrangement. Keep only the latest so we don't
                    // merge "Arrangement" + "Different Chords" into a doubled/sparse timeline.
                    doc.PlaylistItems.Clear();
                    playlistTrackNames.Clear();
                    curPlaylistTrack = 0;
                    playlistItemCount = 0;
                    break;
                case FlpEventId.CurrentPatNum:
                    currentPatNum = FlpEventReader.ReadU16(ev.Data);
                    if (currentPatNum > 0)
                        curPattern = EnsurePattern(patterns, currentPatNum);
                    break;
                case FlpEventId.NewPat:
                {
                    var id = FlpEventReader.ReadU16(ev.Data);
                    // id 0 appears as a section separator in some FL versions — keep current pattern.
                    if (id == 0) break;
                    curPattern = EnsurePattern(patterns, id);
                    currentPatNum = id;
                    break;
                }
                case FlpEventId.PatName:
                    if (curPattern is not null)
                        curPattern.Name = FlpEventReader.ReadUnicode(ev.Data);
                    break;
                case FlpEventId.PatternLength:
                    if (curPattern is not null)
                    {
                        var ticks = FlpEventReader.ReadU32(ev.Data);
                        if (ticks > 0 && doc.Ppq > 0)
                            curPattern.LengthBeats = ticks / doc.Ppq;
                    }
                    break;
                case FlpEventId.PatternNotes:
                    patternNotesCount++;
                    if (curPattern is null && currentPatNum > 0)
                        curPattern = EnsurePattern(patterns, currentPatNum);
                    if (curPattern is not null)
                        ParsePatternNotes(ev.Data, doc.Ppq, curPattern);
                    else
                        patternNotesDropped++;
                    break;
                case FlpEventId.PlaylistItems:
                    playlistEventCount++;
                    playlistItemCount += ParsePlaylistItems(
                        ev.Data, doc.Ppq, versionMajor, channels, patterns, doc, playlistTrackNames);
                    break;
                case FlpEventId.PlaylistTrackName:
                {
                    var name = FlpEventReader.ReadUnicode(ev.Data);
                    if (!string.IsNullOrWhiteSpace(name))
                        playlistTrackNames[curPlaylistTrack++] = name;
                    break;
                }
                case FlpEventId.InsertName:
                    if (curInsert is not null)
                        curInsert.Name = FlpEventReader.ReadUnicode(ev.Data);
                    break;
                case FlpEventId.InsertParams:
                    ParseInsertParams(ev.Data, inserts, ref curInsert, ref curInsertId);
                    break;
                case FlpEventId.InsertRoutes:
                    curInsertId++;
                    if (!inserts.TryGetValue(curInsertId, out curInsert))
                    {
                        curInsert = new ImportTrack
                        {
                            Id = $"insert:{curInsertId}",
                            Name = $"Insert {curInsertId}",
                            Kind = ImportTrackKind.Return,
                            Volume = 0.8
                        };
                        inserts[curInsertId] = curInsert;
                    }
                    break;
                case FlpEventId.InsertFlags:
                    if (curInsert is not null && ev.Data.Length >= 8)
                    {
                        var flags = BitConverter.ToUInt32(ev.Data, 4);
                        curInsert.Soloed = (flags & (1u << 12)) != 0;
                        curInsert.Muted = (flags & (1u << 3)) == 0;
                    }
                    break;
                case FlpEventId.InsertColor:
                    if (curInsert is not null && ev.Data.Length >= 4)
                        curInsert.ColorHex = ColorToHex(FlpEventReader.ReadU32(ev.Data));
                    break;
            }
        }

        doc.Diagnostics["NewChan"] = newChanCount;
        doc.Diagnostics["PatternNotes"] = patternNotesCount;
        doc.Diagnostics["PatternNotesDropped"] = patternNotesDropped;
        doc.Diagnostics["PlaylistEvents"] = playlistEventCount;
        doc.Diagnostics["PlaylistItems"] = playlistItemCount;
        doc.Diagnostics["Channels"] = channels.Count;
        doc.Diagnostics["Patterns"] = patterns.Count;
        doc.Diagnostics["TempoBpmx1000"] = (int)Math.Round(doc.TempoBpm * 1000);
        if (!string.IsNullOrEmpty(doc.SourceVersion))
            doc.Diagnostics["VersionMajor"] = versionMajor;

        // Layer channels don't play themselves — fan pattern notes out to child samplers/synths.
        FanOutLayerPatternNotes(patterns, channels);

        // Channels referenced by notes or playlist must be kept.
        var referencedChannels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in patterns.Values)
        {
            foreach (var id in p.NotesByChannel.Keys)
                referencedChannels.Add(id);
        }
        foreach (var item in doc.PlaylistItems)
        {
            if (!string.IsNullOrEmpty(item.ChannelId))
                referencedChannels.Add(item.ChannelId);
        }

        // Emit mixer: master first, then named/FX inserts, then channels that have content.
        foreach (var insert in inserts.Values.OrderBy(t => ParseIndex(t.Id)))
        {
            if (insert.Kind == ImportTrackKind.Master)
                doc.Tracks.Add(insert);
        }

        foreach (var insert in inserts.Values.OrderBy(t => ParseIndex(t.Id)))
        {
            if (insert.Kind == ImportTrackKind.Master) continue;
            var defaultName = $"Insert {ParseIndex(insert.Id)}";
            if (insert.Devices.Count > 0 || insert.Name != defaultName || insert.Volume is not (0.8 or 1.0))
                doc.Tracks.Add(insert);
        }

        var keptChannels = 0;
        var prunedChannels = 0;
        var automationSkipped = 0;
        foreach (var ch in channels.Values.OrderBy(c => ParseIndex(c.Id)))
        {
            // Automation envelopes aren't imported — drop unreferenced automation channels so
            // large demos don't flood the timeline with empty MIDI lanes.
            if (ch.Kind == ImportTrackKind.Midi && !referencedChannels.Contains(ch.Id))
            {
                automationSkipped++;
                prunedChannels++;
                continue;
            }

            var hasContent =
                referencedChannels.Contains(ch.Id) ||
                !string.IsNullOrEmpty(ch.SamplePath) ||
                ch.Devices.Count > 0 ||
                !IsDefaultChannelName(ch.Name) ||
                ch.Clips.Count > 0;

            if (!hasContent)
            {
                prunedChannels++;
                continue;
            }

            if (ch.MixerInsertIndex is int ins and > 0 && inserts.ContainsKey(ins))
                ch.ParentId = $"insert:{ins}";
            else if (ch.MixerInsertIndex is 0 or null)
                ch.ParentId = null;
            doc.Tracks.Add(ch);
            keptChannels++;
        }

        doc.Diagnostics["ChannelsKept"] = keptChannels;
        doc.Diagnostics["ChannelsPruned"] = prunedChannels;
        if (automationSkipped > 0)
            doc.Diagnostics["AutomationChannelsSkipped"] = automationSkipped;

        foreach (var p in patterns.Values.OrderBy(p => p.Id))
            doc.Patterns.Add(p);

        // Apply playlist track names onto playlist items.
        foreach (var item in doc.PlaylistItems)
        {
            if (playlistTrackNames.TryGetValue(item.PlaylistTrackIndex, out var tn))
                item.PlaylistTrackName = tn;
        }

        doc.Warnings.Add(
            $"FLP import: version={doc.SourceVersion ?? "?"}, tempo={doc.TempoBpm.ToString("0.###", CultureInfo.InvariantCulture)}, " +
            $"channels={keptChannels}/{channels.Count}, patterns={patterns.Count}, " +
            $"notesEvents={patternNotesCount}, playlistItems={playlistItemCount}.");

        if (patternNotesDropped > 0)
            doc.Warnings.Add($"Dropped {patternNotesDropped} pattern-note event(s) with no active pattern context.");

        if (playlistItemCount == 0 && patternNotesCount > 0)
            doc.Warnings.Add("Pattern notes were found but no playlist placements; clips are laid out from bar 1.");

        if (keptChannels == 0)
        {
            doc.Warnings.Add(
                $"FLP contained no usable channels (NewChan={newChanCount}, PatternNotes={patternNotesCount}, PlaylistItems={playlistItemCount}, version={doc.SourceVersion ?? "?"}).");
        }

        return doc;
    }

    private static ImportPattern EnsurePattern(Dictionary<int, ImportPattern> patterns, int id)
    {
        if (!patterns.TryGetValue(id, out var pat))
        {
            pat = new ImportPattern
            {
                Id = $"pat:{id}",
                Name = $"Pattern {id}",
                LengthBeats = 4
            };
            patterns[id] = pat;
        }
        return pat;
    }

    private static bool IsDefaultChannelName(string? name) =>
        string.IsNullOrWhiteSpace(name) ||
        (name.StartsWith("Channel ", StringComparison.Ordinal) &&
         int.TryParse(name.AsSpan("Channel ".Length), out _));

    private static bool IsGeneratorTypeName(string name) =>
        StockInstrumentMap.IsKnownStock("flp", name) ||
        name.Equals("MIDI Out", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Sampler", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("AudioClip", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Effector", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Control Surface", StringComparison.OrdinalIgnoreCase);

    private static void ApplyChanType(ImportTrack channel, byte type, ImportDocument doc)
    {
        switch (type)
        {
            case FlpChanType.Sampler:
                channel.Kind = ImportTrackKind.Instrument;
                break;
            case FlpChanType.Native:
                // Stock synths (3x Osc, FL Keys, Harmless, …) share this type with some audio clips.
                // Default to Instrument; SampleFileName / AudioClip generator can downgrade to Audio.
                channel.Kind = ImportTrackKind.Instrument;
                break;
            case FlpChanType.Layer:
                // Notes are fanned out to ChildChannelIds after parse; Layer itself stays silent.
                channel.Kind = ImportTrackKind.Instrument;
                break;
            case FlpChanType.Instrument:
                channel.Kind = ImportTrackKind.Instrument;
                break;
            case FlpChanType.Automation:
                // Drop from the channel rack unless something references it; envelopes aren't imported yet.
                channel.Kind = ImportTrackKind.Midi;
                break;
            default:
                channel.Kind = ImportTrackKind.Instrument;
                break;
        }
    }

    private static void ApplyLevelsOrBasicParams(ImportTrack channel, byte[] data)
    {
        // PyFLP LevelsEvent (24 bytes): pan Int32 @0 (0..12800, center 6400),
        // volume UInt32 @4 (0..12800, default 10000), pitch_shift Int32 @8 (cents).
        // Legacy BasicChanParams (8 bytes): signed pan @0 (−6400..6400), volume @4.
        if (data.Length < 8) return;

        var panI = BitConverter.ToInt32(data, 0);
        var volI = BitConverter.ToInt32(data, 4);

        // Prefer integer FL scales. Reinterpreting those ints as floats yields denormals
        // (~1e-41) that falsely pass a 0..2 range check and zero out every channel.
        if (data.Length >= 24 && panI is >= 0 and <= 12800 && volI is >= 0 and <= 12800)
        {
            channel.Pan = ClampPanFromFlLevels(panI);
            channel.Volume = ClampVolFromFl(volI);
            channel.PitchCents = BitConverter.ToInt32(data, 8);
            return;
        }

        if (data.Length <= 16 && IsLikelyLegacyPanVol(panI, volI))
        {
            channel.Pan = ClampPanFromFlSigned(panI);
            channel.Volume = ClampVolFromFl(volI);
            return;
        }

        var panF = BitConverter.ToSingle(data, 0);
        var volF = BitConverter.ToSingle(data, 4);
        if (IsPlausibleUnitFloat(panF, allowNegative: true) && IsPlausibleUnitFloat(volF, allowNegative: false))
        {
            channel.Pan = Math.Clamp(panF, -1, 1);
            channel.Volume = Math.Clamp(volF, 0, 1);
        }
    }

    private static bool IsLikelyLegacyPanVol(int pan, int vol) =>
        pan is >= -12800 and <= 12800 && vol is >= 0 and <= 25600 &&
        (Math.Abs(pan) > 10 || vol > 10);

    private static bool IsPlausibleUnitFloat(float f, bool allowNegative)
    {
        if (float.IsNaN(f) || float.IsInfinity(f) || float.IsSubnormal(f)) return false;
        if (allowNegative) return f is >= -1.5f and <= 1.5f && (f == 0f || Math.Abs(f) >= 1e-3f);
        return f is >= 0f and <= 2f && (f == 0f || f >= 1e-3f);
    }

    /// <summary>Channel Parameters blob (168 bytes modern): root note at int32 index 4.</summary>
    private static void ApplyChanParams(ImportTrack channel, byte[] data)
    {
        if (data.Length < 20) return;
        var root = BitConverter.ToInt32(data, 16); // index 4 * 4
        if (root is >= 0 and <= 127)
            channel.RootNote = root;
    }

    /// <summary>Modern FL notes: 24 bytes, rack_channel and key as UInt16 (PyFLP NotesEvent).</summary>
    private static void ParsePatternNotes(byte[] data, double ppq, ImportPattern pattern)
    {
        const int recordSize = 24;
        if (ppq <= 0) ppq = 96;

        for (var i = 0; i + recordSize <= data.Length; i += recordSize)
        {
            var pos = BitConverter.ToInt32(data, i);
            var rackChannel = BitConverter.ToUInt16(data, i + 6);
            var length = BitConverter.ToInt32(data, i + 8);
            var key = BitConverter.ToUInt16(data, i + 12);
            var velocity = data[i + 21];

            var startBeat = pos / ppq;
            var lenBeats = Math.Max(0.01, length / ppq);
            // Step-seq punches can have length 0 — give a 16th.
            if (length == 0) lenBeats = 0.25;

            var note = new ImportNote
            {
                Key = Math.Clamp((int)key, 0, 127),
                StartBeat = startBeat,
                LengthBeats = lenBeats,
                Velocity = Math.Clamp(velocity / 127f, 0, 1)
            };

            var chanId = $"chan:{rackChannel}";
            if (!pattern.NotesByChannel.TryGetValue(chanId, out var list))
            {
                list = new List<ImportNote>();
                pattern.NotesByChannel[chanId] = list;
            }
            list.Add(note);
            pattern.Notes.Add(note);
            pattern.LengthBeats = Math.Max(pattern.LengthBeats, startBeat + lenBeats);
        }
    }

    /// <summary>
    /// FL Layer channels receive pattern notes but don't produce audio — duplicate those notes
    /// onto each child rack channel (samplers / synths under the Layer).
    /// </summary>
    private static void FanOutLayerPatternNotes(
        Dictionary<int, ImportPattern> patterns,
        Dictionary<int, ImportTrack> channels)
    {
        foreach (var (chanIdx, channel) in channels)
        {
            if (channel.ChildChannelIds.Count == 0) continue;
            var layerId = $"chan:{chanIdx}";

            foreach (var pattern in patterns.Values)
            {
                if (!pattern.NotesByChannel.TryGetValue(layerId, out var layerNotes) ||
                    layerNotes.Count == 0)
                    continue;

                foreach (var childIdx in channel.ChildChannelIds)
                {
                    var childId = $"chan:{childIdx}";
                    if (!pattern.NotesByChannel.TryGetValue(childId, out var childNotes))
                    {
                        childNotes = new List<ImportNote>();
                        pattern.NotesByChannel[childId] = childNotes;
                    }

                    foreach (var n in layerNotes)
                    {
                        var copy = new ImportNote
                        {
                            Key = n.Key,
                            StartBeat = n.StartBeat,
                            LengthBeats = n.LengthBeats,
                            Velocity = n.Velocity
                        };
                        childNotes.Add(copy);
                        pattern.Notes.Add(copy);
                    }
                }

                // Avoid empty MIDI clips on the Layer track itself.
                pattern.NotesByChannel.Remove(layerId);
            }
        }
    }

    /// <returns>Number of playlist items accepted.</returns>
    private static int ParsePlaylistItems(
        byte[] data, double ppq, int versionMajor,
        Dictionary<int, ImportTrack> channels,
        Dictionary<int, ImportPattern> patterns,
        ImportDocument doc,
        Dictionary<int, string> playlistTrackNames)
    {
        if (ppq <= 0) ppq = 96;
        if (data.Length == 0) return 0;

        var stride = ChoosePlaylistStride(data.Length, versionMajor);
        if (stride == 0)
        {
            doc.Warnings.Add(
                $"Playlist event size {data.Length} is not a multiple of 32/60/80/88; skipping.");
            return 0;
        }

        doc.Diagnostics["PlaylistStride"] = stride;

        var accepted = 0;
        for (var i = 0; i + stride <= data.Length; i += stride)
        {
            var startTime = BitConverter.ToInt32(data, i);
            var patternBase = BitConverter.ToUInt16(data, i + 4);
            var itemIndex = BitConverter.ToUInt16(data, i + 6);
            var length = BitConverter.ToInt32(data, i + 8);

            // FL20 used Int32 track; FL21+ PyFLP uses UInt16 track_rvidx at +12.
            int trackRaw;
            ushort itemFlags;
            if (stride >= 60 || patternBase >= 20000)
            {
                trackRaw = BitConverter.ToUInt16(data, i + 12);
                itemFlags = BitConverter.ToUInt16(data, i + 18);
            }
            else
            {
                trackRaw = BitConverter.ToInt32(data, i + 12);
                itemFlags = BitConverter.ToUInt16(data, i + 18);
            }

            float startOffset = 0, endOffset = 0;
            if (stride >= 32)
            {
                startOffset = BitConverter.ToSingle(data, i + 24);
                endOffset = BitConverter.ToSingle(data, i + 28);
                if (float.IsNaN(startOffset) || float.IsInfinity(startOffset)) startOffset = 0;
                if (float.IsNaN(endOffset) || float.IsInfinity(endOffset)) endOffset = 0;
            }

            // Skip clearly empty / padding records (common in FL 25/26 80-byte dumps).
            if (length <= 0) continue;
            if (startTime < 0) continue;
            // Modern playlist rows always use pattern_base 20480; other values are padding/noise.
            if (patternBase != 20480 && patternBase != 0) continue;
            if (patternBase == 0 && itemIndex == 0 && startTime == 0) continue;

            var startBeat = startTime / ppq;
            var lenBeats = Math.Max(0.25, length / ppq);
            var muted = (itemFlags & 0x2000) != 0;

            // Reverse playlist track index.
            int trackIndex;
            if (versionMajor >= 21 || patternBase >= 20000 || stride >= 60)
                trackIndex = Math.Max(0, 499 - trackRaw);
            else if (versionMajor >= 20)
                trackIndex = Math.Max(0, 198 - trackRaw);
            else
                trackIndex = Math.Abs(trackRaw);

            var baseVal = patternBase == 0 ? (ushort)20480 : patternBase;
            var isPattern = itemIndex > baseVal;

            if (isPattern)
            {
                var pid = itemIndex - baseVal;
                if (pid <= 0 || pid > 9999) continue;
                // Reject absurdly long/far placements from misaligned strides.
                if (startBeat > 1_000_000 || lenBeats > 1_000_000) continue;

                var pat = EnsurePattern(patterns, pid);
                doc.PlaylistItems.Add(new ImportPlaylistItem
                {
                    StartBeat = startBeat,
                    LengthBeats = lenBeats,
                    Muted = muted,
                    PlaylistTrackIndex = trackIndex,
                    PlaylistTrackName = playlistTrackNames.GetValueOrDefault(trackIndex),
                    PatternId = pat.Id,
                    StartOffsetBeats = startOffset,
                    EndOffsetBeats = endOffset
                });
                accepted++;
            }
            else
            {
                // Channel playlist rows are audio/automation clips — not generator channels.
                if (patternBase != 20480) continue;
                if (itemIndex > 500) continue;
                if (!channels.TryGetValue(itemIndex, out var ch)) continue;

                var isAudioClip = ch.Kind == ImportTrackKind.Audio || !string.IsNullOrEmpty(ch.SamplePath);
                if (!isAudioClip) continue; // skip MIDI Out / synth / empty generator rows
                if (startBeat > 1_000_000 || lenBeats > 1_000_000) continue;

                doc.PlaylistItems.Add(new ImportPlaylistItem
                {
                    StartBeat = startBeat,
                    LengthBeats = lenBeats,
                    Muted = muted,
                    PlaylistTrackIndex = trackIndex,
                    PlaylistTrackName = playlistTrackNames.GetValueOrDefault(trackIndex),
                    ChannelId = ch.Id,
                    SamplePath = ch.SamplePath,
                    IsAudio = true,
                    StartOffsetBeats = startOffset,
                    EndOffsetBeats = endOffset
                });
                accepted++;
            }
        }

        return accepted;
    }

    /// <summary>Pick playlist record stride: 32 (legacy), 60 (FL21), 80 (FL25), or 88 (FL25/26 edited).</summary>
    private static int ChoosePlaylistStride(int length, int versionMajor)
    {
        var candidates = new List<int>();
        // Prefer larger modern strides first when several divide evenly — 37840 % 80 == 0 and
        // % 88 == 0, but only 88 is the true FL26 record size for this dump.
        if (length % 88 == 0) candidates.Add(88);
        if (length % 80 == 0) candidates.Add(80);
        if (length % 60 == 0) candidates.Add(60);
        if (length % 32 == 0) candidates.Add(32);
        if (candidates.Count == 0) return 0;
        if (candidates.Count == 1) return candidates[0];

        if (versionMajor >= 25)
        {
            if (candidates.Contains(88)) return 88;
            if (candidates.Contains(80)) return 80;
        }
        if (versionMajor >= 21 && candidates.Contains(60)) return 60;
        if (candidates.Contains(32)) return 32;
        return candidates[0];
    }

    private static void ParseInsertParams(
        byte[] data,
        Dictionary<int, ImportTrack> inserts,
        ref ImportTrack? curInsert,
        ref int curInsertId)
    {
        const int stride = 12;
        for (var i = 0; i + stride <= data.Length; i += stride)
        {
            var messageId = data[i + 4];
            var channelData = BitConverter.ToUInt16(data, i + 6);
            var messageData = BitConverter.ToInt32(data, i + 8);
            var insertId = (channelData >> 6) & 0x7F;

            if (!inserts.TryGetValue(insertId, out var insert))
            {
                insert = new ImportTrack
                {
                    Id = $"insert:{insertId}",
                    Name = insertId == 0 ? "Master" : $"Insert {insertId}",
                    Kind = insertId == 0 ? ImportTrackKind.Master : ImportTrackKind.Return,
                    Volume = 0.8
                };
                inserts[insertId] = insert;
            }

            curInsert = insert;
            curInsertId = insertId;

            if (messageId == 0xC0)
                insert.Volume = ClampVolFromFl(messageData);
            else if (messageId == 0xC1)
                insert.Pan = ClampPanFromFl(messageData);
            else if (messageId >= 64 && messageId <= 64 + 104)
            {
                // Send level toward another insert. FL stores a slot for every destination;
                // skip silent routes so we don't create dense feedback-prone send graphs.
                var dest = messageId - 64;
                if (dest == insertId) continue;
                var level = ClampVolFromFl(messageData);
                if (level < 0.001) continue;
                insert.Sends.Add(new ImportSend
                {
                    TargetTrackId = $"insert:{dest}",
                    Level = level
                });
            }
        }
    }

    private static string ResolveSamplePath(string sample, string projectDir)
    {
        if (string.IsNullOrWhiteSpace(sample)) return sample;
        try
        {
            sample = FlStudioPaths.ExpandPlaceholders(sample);

            if (Path.IsPathRooted(sample) && File.Exists(sample)) return Path.GetFullPath(sample);

            // Still unresolved placeholder — don't treat it as a relative path.
            if (sample.Contains('%')) return sample;

            var combined = Path.GetFullPath(Path.Combine(projectDir, sample));
            if (File.Exists(combined)) return combined;

            var fileName = Path.GetFileName(sample);
            if (string.IsNullOrEmpty(fileName)) return sample;

            var audioSub = Path.GetFullPath(Path.Combine(projectDir, "Audio", fileName));
            if (File.Exists(audioSub)) return audioSub;

            var inProject = Path.Combine(projectDir, fileName);
            if (File.Exists(inProject)) return inProject;

            // Shallow search only — never walk the whole Documents/Home tree (hangs on macOS).
            if (Directory.Exists(projectDir) && !IsHugeRootFolder(projectDir))
            {
                foreach (var dir in SafeImmediateSubdirs(projectDir))
                {
                    var candidate = Path.Combine(dir, fileName);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch
        {
            // keep original
        }
        return sample;
    }

    private static bool IsHugeRootFolder(string dir)
    {
        try
        {
            var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return string.Equals(full, home, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(full, docs, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(full, desktop, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(full, "/", StringComparison.Ordinal);
        }
        catch
        {
            return true;
        }
    }

    private static IEnumerable<string> SafeImmediateSubdirs(string projectDir)
    {
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(projectDir); }
        catch { yield break; }

        var n = 0;
        foreach (var d in dirs)
        {
            if (n++ > 64) yield break;
            yield return d;
        }
    }

    private static bool IsLikelyThirdPartyGenerator(string name) =>
        !StockInstrumentMap.IsKnownStock("flp", name) &&
        !name.Equals("Sampler", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("AudioClip", StringComparison.OrdinalIgnoreCase) &&
        !name.StartsWith("Fruity", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("MIDI Out", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSampleOrDisplayName(string name)
    {
        if (name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".wv", StringComparison.OrdinalIgnoreCase) ||
            name.Contains('/') || name.Contains('\\') ||
            name.Contains("%FLStudio", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>True for Fruity/stock FX names or typical VST identifiers — not channel labels.</summary>
    private static bool LooksLikeRealPluginName(string name)
    {
        if (StockEffectMap.TryMap("flp", name, out _)) return true;
        if (StockInstrumentMap.IsKnownStock("flp", name)) return true;
        if (name.StartsWith("Fruity", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".component", StringComparison.OrdinalIgnoreCase))
            return true;
        // Common FL stock non-Fruity devices.
        if (name.Equals("Soundgoodizer", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Maximus", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Gross Beat", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Edison", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Transient Processor", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Wave Candy", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Control Surface", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Effector", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static double ClampVolFromFl(int raw)
    {
        if (raw <= 0) return 0;
        var v = raw / 12800.0;
        return Math.Clamp(v, 0, 1);
    }

    /// <summary>LevelsEvent pan: 0..12800 with center 6400.</summary>
    private static double ClampPanFromFlLevels(int raw) =>
        Math.Clamp((raw - 6400) / 6400.0, -1, 1);

    /// <summary>Legacy signed pan: −6400..6400 with center 0.</summary>
    private static double ClampPanFromFlSigned(int raw) =>
        Math.Clamp(raw / 6400.0, -1, 1);

    private static double ClampPanFromFl(int raw) => ClampPanFromFlSigned(raw);

    private static string ColorToHex(uint rgba)
    {
        var r = rgba & 0xFF;
        var g = (rgba >> 8) & 0xFF;
        var b = (rgba >> 16) & 0xFF;
        return $"#{r:x2}{g:x2}{b:x2}";
    }

    private static int ParseIndex(string id)
    {
        var colon = id.LastIndexOf(':');
        if (colon >= 0 && int.TryParse(id[(colon + 1)..], out var n)) return n;
        return 0;
    }
}
