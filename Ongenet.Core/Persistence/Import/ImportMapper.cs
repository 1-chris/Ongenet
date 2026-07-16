using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Music;

namespace Ongenet.Core.Persistence.Import;

/// <summary>Maps an <see cref="ImportDocument"/> into an Ongenet <see cref="Project"/>.</summary>
public sealed class ImportMapper
{
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;
    private readonly IAudioFileService? _audioFiles;
    private Dictionary<string, LoadedAudio?>? _audioCache;
    private readonly bool _eagerDecodeAudio;

    public ImportMapper(
        IInstrumentRegistry instruments,
        IEffectRegistry effects,
        IAudioFileService? audioFiles = null,
        bool eagerDecodeAudio = false)
    {
        _instruments = instruments;
        _effects = effects;
        _audioFiles = audioFiles;
        _eagerDecodeAudio = eagerDecodeAudio;
    }

    public ImportResult Map(ImportDocument doc, string sourceFormat, string sourcePath)
    {
        _audioCache = new Dictionary<string, LoadedAudio?>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        var warnings = new List<string>(doc.Warnings);

        if (doc.Diagnostics.Count > 0)
        {
            warnings.Add(
                "Import diagnostics: " +
                string.Join(", ", doc.Diagnostics.Select(kv => $"{kv.Key}={kv.Value}")));
        }

        var project = new Project
        {
            Name = string.IsNullOrWhiteSpace(doc.Name)
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : doc.Name,
            Tempo = new Tempo(doc.TempoBpm > 0 ? doc.TempoBpm : 120),
            TimeSignature = new TimeSignature(
                doc.TimeSigNumerator > 0 ? doc.TimeSigNumerator : 4,
                doc.TimeSigDenominator > 0 ? doc.TimeSigDenominator : 4)
        };

        var master = new Track
        {
            Name = "Master",
            Kind = TrackKind.Master,
            ColorKey = "CatppuccinSubtext0",
            Volume = 1.0
        };
        project.Tracks.Add(master);

        var idMap = new Dictionary<string, Guid>(StringComparer.Ordinal);
        idMap[""] = master.Id;

        var trackOrder = new List<(ImportTrack Src, Track Dest)>();
        foreach (var src in doc.Tracks)
        {
            if (src.Kind == ImportTrackKind.Master)
            {
                ApplyMix(master, src);
                MapEffects(master, src, sourceFormat, warnings);
                idMap[src.Id] = master.Id;
                continue;
            }

            var track = new Track
            {
                Name = string.IsNullOrWhiteSpace(src.Name) ? "Track" : src.Name,
                Kind = MapKind(src.Kind),
                ColorKey = src.ColorHex is { Length: > 0 } hex ? hex : "CatppuccinMauve"
            };
            ApplyMix(track, src);
            idMap[src.Id] = track.Id;
            trackOrder.Add((src, track));
            project.Tracks.Add(track);
        }

        foreach (var (src, track) in trackOrder)
        {
            if (!string.IsNullOrEmpty(src.ParentId) && idMap.TryGetValue(src.ParentId, out var parentId))
                track.ParentId = parentId;

            if (track.Kind is TrackKind.Instrument or TrackKind.Hybrid)
                AttachInstrument(track, src, sourceFormat, unresolved, warnings);

            foreach (var clip in src.Clips.Where(c => string.IsNullOrEmpty(c.PatternId)))
                track.Clips.Add(MapClip(clip, unresolved, warnings));

            MapEffects(track, src, sourceFormat, warnings);
            track.CommitInstruments();
            track.CommitEffects();
        }

        foreach (var (src, track) in trackOrder)
        {
            foreach (var send in src.Sends)
            {
                if (!idMap.TryGetValue(send.TargetTrackId, out var targetId))
                {
                    warnings.Add($"Send from '{track.Name}' references unknown target '{send.TargetTrackId}'.");
                    continue;
                }

                track.Sends.Add(new TrackSend
                {
                    TargetTrackId = targetId,
                    Level = Math.Clamp(send.Level, 0, 1),
                    PreFader = send.Prefader
                });
            }
        }

        var patternById = doc.Patterns.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var patternIdMap = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var p in doc.Patterns)
        {
            var pattern = new Pattern
            {
                Name = p.Name,
                LengthBeats = p.LengthBeats > 0 ? p.LengthBeats : 4
            };
            patternIdMap[p.Id] = pattern.Id;
            project.Patterns.Add(pattern);
        }

        // Primary audible path: expand playlist pattern/audio items onto instrument/audio tracks.
        ExpandPlaylistItems(doc, project, idMap, patternById, patternIdMap, unresolved, warnings);

        // Legacy: pattern clips embedded on import tracks.
        Track? patternLane = null;
        foreach (var (src, _) in trackOrder)
        {
            foreach (var clip in src.Clips.Where(c => !string.IsNullOrEmpty(c.PatternId)))
            {
                if (clip.PatternId is null || !patternById.TryGetValue(clip.PatternId, out var pat))
                    continue;

                ExpandPatternPlacement(
                    project, idMap, pat, clip.StartBeat, clip.LengthBeats, clip.Name,
                    patternIdMap, ref patternLane, warnings);
            }
        }

        // Patterns with notes but no playlist: place once from beat 0 on each channel.
        if (doc.PlaylistItems.Count == 0)
        {
            foreach (var p in doc.Patterns)
            {
                ExpandPatternPlacement(
                    project, idMap, p, 0, p.LengthBeats, p.Name,
                    patternIdMap, ref patternLane, warnings);
            }
        }

        var maxBeat = project.Tracks.SelectMany(t => t.Clips).Select(c => c.EndBeat).DefaultIfEmpty(16).Max();
        maxBeat = Math.Max(maxBeat, project.PatternClips.Select(c => c.StartBeat + c.LengthBeats).DefaultIfEmpty(0).Max());
        project.BarCount = Math.Max(16, (int)Math.Ceiling(maxBeat / Math.Max(1, project.TimeSignature.Numerator)) + 1);

        // Empty arrangement lanes open compact (half of the 64px default track row).
        const double emptyLaneHeight = 32;
        var tracksWithPatternClips = new HashSet<Guid>(project.PatternClips.Select(pc => pc.TrackId));
        foreach (var track in project.Tracks)
        {
            if (track.Kind is TrackKind.Master or TrackKind.Group) continue;
            if (track.Clips.Count > 0 || tracksWithPatternClips.Contains(track.Id)) continue;
            if (track.LaneHeight > 0) continue;
            track.LaneHeight = emptyLaneHeight;
        }

        if (project.Tracks.Count <= 1)
            warnings.Add("Import produced no content tracks (master only).");

        return new ImportResult
        {
            Project = project,
            SourceFormat = sourceFormat,
            SourcePath = sourcePath,
            Warnings = warnings,
            UnresolvedSamplePaths = unresolved
        };
    }

    private void ExpandPlaylistItems(
        ImportDocument doc,
        Project project,
        Dictionary<string, Guid> idMap,
        Dictionary<string, ImportPattern> patternById,
        Dictionary<string, Guid> patternIdMap,
        List<string> unresolved,
        List<string> warnings)
    {
        Track? patternLane = null;
        foreach (var item in doc.PlaylistItems)
        {
            if (item.Muted) continue;

            if (!string.IsNullOrEmpty(item.PatternId) &&
                patternById.TryGetValue(item.PatternId, out var pat))
            {
                var name = item.PlaylistTrackName is { Length: > 0 } n
                    ? $"{pat.Name} ({n})"
                    : pat.Name;
                ExpandPatternPlacement(
                    project, idMap, pat, item.StartBeat, item.LengthBeats, name,
                    patternIdMap, ref patternLane, warnings);
                continue;
            }

            if (string.IsNullOrEmpty(item.ChannelId) || !idMap.TryGetValue(item.ChannelId, out var trackId))
                continue;

            var dest = project.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (dest is null) continue;

            if (item.IsAudio || !string.IsNullOrEmpty(item.SamplePath))
            {
                if (dest.Kind == TrackKind.Instrument)
                    dest.Kind = TrackKind.Hybrid;
                else if (dest.Kind != TrackKind.Hybrid)
                    dest.Kind = TrackKind.Audio;

                var clip = new ImportClip
                {
                    Name = dest.Name,
                    StartBeat = item.StartBeat,
                    LengthBeats = item.LengthBeats,
                    IsAudio = true,
                    SamplePath = item.SamplePath,
                    StretchToTempo = true,
                    SourceOffsetSeconds = item.StartOffsetBeats > 0
                        ? item.StartOffsetBeats * (60.0 / Math.Max(1, doc.TempoBpm))
                        : 0
                };
                dest.Clips.Add(MapClip(clip, unresolved, warnings));
            }
        }
    }

    private void ExpandPatternPlacement(
        Project project,
        Dictionary<string, Guid> idMap,
        ImportPattern pat,
        double startBeat,
        double lengthBeats,
        string clipName,
        Dictionary<string, Guid> patternIdMap,
        ref Track? patternLane,
        List<string> warnings)
    {
        var groups = pat.NotesByChannel.Count > 0
            ? pat.NotesByChannel
            : (pat.ChannelId is { } cid && pat.Notes.Count > 0
                ? new Dictionary<string, List<ImportNote>> { [cid] = pat.Notes }
                : new Dictionary<string, List<ImportNote>>());

        // If only flat Notes without ChannelId, skip channel expansion (still add PatternClip).
        foreach (var (chanId, notes) in groups)
        {
            if (notes.Count == 0) continue;
            if (!idMap.TryGetValue(chanId, out var trackId))
            {
                warnings.Add($"Pattern '{pat.Name}' references missing channel '{chanId}'.");
                continue;
            }

            var dest = project.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (dest is null) continue;
            if (dest.Kind == TrackKind.Audio)
                dest.Kind = TrackKind.Hybrid;
            else if (dest.Kind is not (TrackKind.Instrument or TrackKind.Hybrid))
                dest.Kind = TrackKind.Instrument;

            if (dest.Instruments.Count == 0)
            {
                // Prefer stock mapping from the destination track name when pattern expansion
                // runs before/without a prior AttachInstrument (e.g. late kind promotion).
                var typeId = BasicSamplerInstrument.TypeId;
                if (StockInstrumentMap.TryMap("flp", dest.Name, out var mapped))
                    typeId = mapped;
                if (TryCreateInstrument(typeId) is { } fallback)
                {
                    dest.Instruments.Add(new InstrumentSlot(fallback));
                    dest.CommitInstruments();
                }
            }

            var clipLen = lengthBeats > 0 ? lengthBeats : Math.Max(pat.LengthBeats, notes.Max(n => n.StartBeat + n.LengthBeats));
            var midiClip = new Clip
            {
                Name = clipName,
                StartBeat = startBeat,
                LengthBeats = clipLen,
                IsAudio = false
            };

            foreach (var n in notes)
            {
                // Keep notes that start within the placed length; clip-relative.
                if (n.StartBeat >= clipLen) continue;
                midiClip.Notes.Add(new MidiNote
                {
                    Note = Math.Clamp(n.Key, 0, 127),
                    StartBeat = n.StartBeat,
                    LengthBeats = Math.Max(0.01, Math.Min(n.LengthBeats, clipLen - n.StartBeat)),
                    Velocity = Math.Clamp(n.Velocity, 0, 1)
                });
            }

            if (midiClip.Notes.Count > 0)
                dest.Clips.Add(midiClip);
        }

        if (patternIdMap.TryGetValue(pat.Id, out var pid))
        {
            patternLane ??= EnsurePatternTrack(project);
            project.PatternClips.Add(new PatternClip
            {
                PatternId = pid,
                TrackId = patternLane.Id,
                StartBeat = startBeat,
                LengthBeats = lengthBeats > 0 ? lengthBeats : pat.LengthBeats
            });
        }
    }

    private void AttachInstrument(
        Track track,
        ImportTrack src,
        string sourceFormat,
        List<string> unresolved,
        List<string> warnings)
    {
        if (track.Instruments.Count > 0) return;

        foreach (var skipped in src.Devices.Where(d => d.IsInstrument && d.IsThirdParty))
            warnings.Add($"Skipped third-party instrument '{skipped.Name}' on '{track.Name}'.");

        string? typeId = null;
        // Prefer a concrete stock generator (3x Osc, GMS, …) over a placeholder Sampler entry.
        var stockDevice = src.Devices
            .Where(d => d.IsInstrument && !d.IsThirdParty)
            .OrderByDescending(d => !d.Name.Equals("Sampler", StringComparison.OrdinalIgnoreCase) &&
                                    !d.Name.Equals("AudioClip", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (stockDevice is not null &&
            stockDevice.Name.Equals("MIDI Out", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Skipped MIDI Out on '{track.Name}' (no local instrument).");
            return;
        }

        if (stockDevice is not null)
        {
            if (!StockInstrumentMap.TryMap(sourceFormat, stockDevice.Name, out typeId))
            {
                warnings.Add($"Unmapped stock instrument '{stockDevice.Name}' on '{track.Name}' — using Basic Sampler.");
                typeId = BasicSamplerInstrument.TypeId;
            }
        }
        else if (src.Devices.Any(d => d.IsInstrument && d.IsThirdParty))
        {
            // Keep a silent placeholder so arrangement clips still have a destination track.
            typeId = BasicSamplerInstrument.TypeId;
        }
        else if (!string.IsNullOrEmpty(src.SamplePath))
        {
            typeId = BasicSamplerInstrument.TypeId;
        }
        else
        {
            // MIDI channel with no generator metadata — still need a playable voice.
            typeId = BasicSamplerInstrument.TypeId;
        }

        if (TryCreateInstrument(typeId) is not { } instrument)
        {
            warnings.Add($"Could not create instrument '{typeId}' for '{track.Name}'.");
            return;
        }

        LoadSampleIntoHost(instrument, src.SamplePath, unresolved, warnings);
        ApplyImportedChannelParams(instrument, src);
        track.Instruments.Add(new InstrumentSlot(instrument));
    }

    private static void ApplyImportedChannelParams(IInstrument instrument, ImportTrack src)
    {
        if (instrument is BasicSamplerInstrument sampler)
        {
            sampler.FinePitchCents = src.PitchCents;
            // FL channel-rack one-shots are typically short; keep release tight.
            if (!string.IsNullOrEmpty(src.SamplePath))
                sampler.ReleaseSeconds = Math.Min(sampler.ReleaseSeconds, 0.06);
            return;
        }

        if (instrument is TripleOscInstrument osc)
        {
            if (src.FilterCutoff is int cut)
                osc.Cutoff = FlCutoffToHz(cut);
            if (src.FilterResonance is int reso)
                osc.Resonance = 0.5 + Math.Clamp(reso / 1024.0, 0, 1) * 10;

            // Mild default stack so mapped 3x Osc isn't a static mono saw.
            if (Math.Abs(osc.Fine2) < 0.01) osc.Fine2 = -8;
            if (Math.Abs(osc.Fine3) < 0.01) osc.Fine3 = 11;
            if (osc.Level2 < 0.05) osc.Level2 = 0.65;
            if (osc.Level3 < 0.05) osc.Level3 = 0.45;
            return;
        }

        if (instrument is PolymerInstrument polymer)
        {
            if (src.FilterCutoff is int polyCut)
                polymer.Cutoff = FlCutoffToHz(polyCut);
        }
    }

    /// <summary>Map FL channel-rack cutoff (0..1024) onto an exponential 20 Hz..20 kHz range.</summary>
    private static double FlCutoffToHz(int flCutoff)
    {
        var t = Math.Clamp(flCutoff / 1024.0, 0, 1);
        return 20.0 * Math.Pow(1000.0, t);
    }

    private void LoadSampleIntoHost(
        IInstrument instrument,
        string? samplePath,
        List<string> unresolved,
        List<string> warnings)
    {
        if (string.IsNullOrEmpty(samplePath)) return;
        if (instrument is not ISampleHost host) return;

        var resolved = ResolveExisting(samplePath);
        if (resolved is null)
        {
            unresolved.Add(samplePath);
            warnings.Add($"Sample not found: {samplePath}");
            return;
        }

        if (instrument is BasicSamplerInstrument sampler)
            sampler.SampleFilePath = resolved;

        if (!_eagerDecodeAudio)
            return;

        var loaded = LoadAudioCached(resolved);
        if (loaded is not null)
            host.LoadSample(loaded.Samples, Path.GetFileName(resolved));
        else
            warnings.Add($"Failed to decode sample: {resolved}");
    }

    private Clip MapClip(ImportClip src, List<string> unresolved, List<string> warnings)
    {
        var clip = new Clip
        {
            Name = src.Name,
            StartBeat = src.StartBeat,
            LengthBeats = src.LengthBeats > 0 ? src.LengthBeats : 4,
            IsAudio = src.IsAudio,
            SourceOffsetSeconds = src.SourceOffsetSeconds,
            SourceLengthSeconds = src.SourceLengthSeconds,
            StretchToTempo = src.StretchToTempo
        };

        if (src.IsAudio && !string.IsNullOrEmpty(src.SamplePath))
        {
            var resolved = ResolveExisting(src.SamplePath);
            clip.AudioFilePath = resolved ?? src.SamplePath;
            if (resolved is null)
            {
                unresolved.Add(src.SamplePath);
                warnings.Add($"Sample not found: {src.SamplePath}");
            }
            else if (_eagerDecodeAudio)
            {
                if (LoadAudioCached(resolved) is { } loaded)
                {
                    clip.Samples = loaded.Samples;
                    clip.Waveform = loaded.Waveform;
                    clip.SourceTempo = loaded.Tempo;
                }
                else
                {
                    warnings.Add($"Failed to decode sample: {resolved}");
                }
            }
        }

        foreach (var n in src.Notes)
        {
            clip.Notes.Add(new MidiNote
            {
                Note = Math.Clamp(n.Key, 0, 127),
                StartBeat = n.StartBeat,
                LengthBeats = Math.Max(0.01, n.LengthBeats),
                Velocity = Math.Clamp(n.Velocity, 0, 1)
            });
        }

        foreach (var w in src.WarpMarkers)
        {
            clip.WarpMarkers.Add(new WarpMarker
            {
                BeatPosition = w.BeatTime,
                SourceSeconds = w.SourceSeconds
            });
        }

        return clip;
    }

    private void MapEffects(Track track, ImportTrack src, string sourceFormat, List<string> warnings)
    {
        foreach (var device in src.Devices)
        {
            if (device.IsInstrument) continue;

            if (device.IsThirdParty)
            {
                warnings.Add($"Skipped third-party effect '{device.Name}' on '{track.Name}'.");
                continue;
            }

            if (!StockEffectMap.TryMap(sourceFormat, device.Name, out var typeId))
            {
                warnings.Add($"Unmapped stock effect '{device.Name}' on '{track.Name}'.");
                continue;
            }

            // Cap stock FX creation — large FL projects attach dozens of Fruity inserts;
            // instantiating them all makes the post-import UI rebuild very expensive.
            const int maxFxPerTrack = 8;
            if (track.Effects.Count >= maxFxPerTrack)
            {
                warnings.Add($"Skipped additional stock effect '{device.Name}' on '{track.Name}' (import FX cap).");
                continue;
            }

            IAudioEffect? fx;
            try { fx = _effects.Create(typeId); }
            catch (ArgumentException)
            {
                warnings.Add($"Ongenet effect '{typeId}' unavailable for '{device.Name}'.");
                continue;
            }

            ApplyDefaultStockEffectParams(fx, device.Name);

            foreach (var (pname, value) in device.Parameters)
            {
                var param = fx.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, pname, StringComparison.OrdinalIgnoreCase));
                if (param is FloatParameter fp)
                    fp.Value = fp.Min + Math.Clamp(value, 0, 1) * (fp.Max - fp.Min);
            }

            track.Effects.Add(fx);
        }
    }

    /// <summary>Heuristic defaults so FL stock FX aren't all identical Ongenet presets.</summary>
    private static void ApplyDefaultStockEffectParams(IAudioEffect fx, string flName)
    {
        if (fx is ReverbEffect reverb &&
            (flName.Contains("Reverb", StringComparison.OrdinalIgnoreCase) ||
             flName.Contains("Reeverb", StringComparison.OrdinalIgnoreCase)))
        {
            // FL insert reverbs are often wetter than our 0.3 Mix default.
            if (Math.Abs(reverb.Mix - 0.3) < 0.01) reverb.Mix = 0.35;
        }
    }

    private LoadedAudio? LoadAudioCached(string path)
    {
        _audioCache ??= new Dictionary<string, LoadedAudio?>(StringComparer.Ordinal);
        if (_audioCache.TryGetValue(path, out var cached))
            return cached;

        LoadedAudio? loaded = null;
        try
        {
            // Skip QueenMary tempo analysis — factory packs + many clips make it the import bottleneck.
            loaded = _audioFiles?.Load(path, analyzeTempo: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Import audio load failed for '{path}': {ex.Message}");
        }

        _audioCache[path] = loaded;
        return loaded;
    }

    private IInstrument? TryCreateInstrument(string typeId)
    {
        try { return _instruments.Create(typeId); }
        catch (ArgumentException) { return null; }
    }

    private static void ApplyMix(Track track, ImportTrack src)
    {
        track.Volume = Math.Clamp(src.Volume, 0, 1);
        track.Pan = Math.Clamp(src.Pan, -1, 1);
        track.IsMuted = src.Muted;
        track.IsSoloed = src.Soloed;
    }

    private static TrackKind MapKind(ImportTrackKind kind) => kind switch
    {
        ImportTrackKind.Instrument => TrackKind.Instrument,
        ImportTrackKind.Midi => TrackKind.Midi,
        ImportTrackKind.Group => TrackKind.Group,
        ImportTrackKind.Return => TrackKind.Return,
        ImportTrackKind.Master => TrackKind.Master,
        _ => TrackKind.Audio
    };

    private static Track EnsurePatternTrack(Project project)
    {
        var existing = project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Pattern);
        if (existing is not null) return existing;
        var t = new Track { Name = "Patterns", Kind = TrackKind.Pattern, ColorKey = "CatppuccinPeach" };
        project.Tracks.Add(t);
        return t;
    }

    private static string? ResolveExisting(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        catch
        {
            // ignore invalid paths
        }
        return null;
    }
}
