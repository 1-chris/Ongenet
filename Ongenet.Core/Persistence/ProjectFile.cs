using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Music;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Persistence;

/// <summary>
/// Reads and writes the single-file <c>.ongen</c> project format: a ZIP archive containing a manifest, the
/// project document (a chunked binary built with <see cref="OngenWriter"/>/<see cref="OngenReader"/>) and one
/// de-duplicated float32 WAV per unique sample. Designed to load older/newer versions opportunistically —
/// unknown chunks and trailing fields are skipped, unavailable instruments/effects/samples are reported as
/// warnings rather than failing the load.
/// </summary>
public static class ProjectFile
{
    /// <summary>Bumped whenever the on-disk layout changes. Newer files opened in an older app degrade gracefully.</summary>
    /// <remarks>v2: instrument rack. v3: track routing. v4: patterns, session, warp, takes, multi-out, MPE/groove/drum. v5: pattern tracks, pattern row metadata. v6: ARA pitch offset. v7: poly pitch segments.</remarks>
    public const int FormatVersion = 23;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ONGENPRJ"); // 8 bytes
    private const string ManifestEntry = "ongen.manifest";
    private const string ProjectEntry = "project.dat";

    public sealed record LoadResult(
        Project Project,
        double LoopStart,
        double LoopEnd,
        double StartBeat,
        IReadOnlyList<string> Warnings,
        bool FromNewerVersion);

    // ----------------------------------------------------------------- Save

    public static void Save(Project project, Stream output, string appVersion,
        double loopStart, double loopEnd, double startBeat)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        // Serialize the document to memory first, collecting/deduplicating samples as we go.
        var store = new SampleStore();
        using var doc = new MemoryStream();
        using (var w = new OngenWriter(doc))
            WriteProject(w, project, store, loopStart, loopEnd, startBeat);

        WriteEntry(zip, ManifestEntry, s =>
        {
            using var bw = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
            bw.Write(Magic);
            bw.Write(FormatVersion);
            bw.Write(appVersion ?? "");
            bw.Write(DateTime.UtcNow.Ticks);
        });

        WriteEntry(zip, ProjectEntry, s => doc.WriteTo(s));

        // Float audio barely deflates and Optimal is slow on large projects — use Fastest so big saves
        // finish quickly (smaller window for an interrupted write).
        foreach (var (hash, buffer) in store.Entries)
            WriteEntry(zip, $"samples/{hash}.wav", s => WavStream.WriteFloat32(s, buffer), CompressionLevel.Fastest);
    }

    private static void WriteEntry(ZipArchive zip, string name, Action<Stream> body,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var s = entry.Open();
        body(s);
    }

    private static void WriteProject(OngenWriter w, Project p, SampleStore store,
        double loopStart, double loopEnd, double startBeat)
    {
        w.WriteChunk(c =>
        {
            c.WriteString(p.Name);
            c.WriteDouble(p.Tempo.BeatsPerMinute);
            c.WriteInt(p.TimeSignature.Numerator);
            c.WriteInt(p.TimeSignature.Denominator);
            c.WriteInt(p.BarCount);
            c.WriteDouble(loopStart);
            c.WriteDouble(loopEnd);
            c.WriteDouble(startBeat);
        });

        w.WriteInt(p.Tracks.Count);
        foreach (var t in p.Tracks) WriteTrack(w, t, store);

        // MIDI-controller mappings: a trailing self-describing chunk so older readers (which stop after
        // the tracks) ignore it, and newer readers skip it gracefully when an old file lacks it.
        WriteMidiMappings(w, p);
        WriteProjectExtensions(w, p);
    }

    private static void WriteProjectExtensions(OngenWriter w, Project p)
    {
        w.WriteChunk(c =>
        {
            // Patterns
            c.WriteInt(p.Patterns.Count);
            foreach (var pat in p.Patterns) WritePattern(c, pat);

            c.WriteInt(p.PatternClips.Count);
            foreach (var pc in p.PatternClips)
            {
                c.WriteGuid(pc.Id);
                c.WriteGuid(pc.PatternId);
                c.WriteGuid(pc.TrackId);
                c.WriteDouble(pc.StartBeat);
                c.WriteDouble(pc.LengthBeats);
            }

            // Session clips
            c.WriteInt(p.SessionClips.Count);
            foreach (var sc in p.SessionClips)
            {
                c.WriteGuid(sc.Id);
                c.WriteGuid(sc.TrackId);
                c.WriteInt(sc.SceneIndex);
                c.WriteString(sc.Name);
                c.WriteDouble(sc.LengthBeats);
                c.WriteInt((int)sc.LaunchMode);
                c.WriteNullableGuid(sc.SourceClipId);
            }

            // Multi-out routes
            c.WriteInt(p.MultiOutputRoutes.Count);
            foreach (var r in p.MultiOutputRoutes)
            {
                c.WriteGuid(r.SourceTrackId);
                c.WriteInt(r.SlotIndex);
                c.WriteInt(r.PluginOutputBus);
                c.WriteGuid(r.DestinationTrackId);
                c.WriteDouble(r.Level);
            }

            // MPE
            c.WriteBool(p.Mpe.Enabled);
            c.WriteInt(p.Mpe.MasterChannel);
            c.WriteInt(p.Mpe.MemberChannelStart);
            c.WriteInt(p.Mpe.MemberChannelCount);

            // Active groove (optional)
            if (p.ActiveGroove is { } g)
            {
                c.WriteBool(true);
                c.WriteGuid(g.Id);
                c.WriteString(g.Name);
                c.WriteDouble(g.SwingAmount);
                c.WriteInt(g.Division);
            }
            else c.WriteBool(false);

            // Drum maps
            c.WriteInt(p.DrumMaps.Count);
            foreach (var dm in p.DrumMaps) WriteDrumMap(c, dm);

            // Legacy video tracks slot (empty for v11+; layers stored below)
            c.WriteInt(0);

            c.WriteInt((int)p.PlaybackMode);
            c.WriteDouble(p.LaunchQuantizeBeats);

            c.WriteInt(p.SessionClips.Count);
            foreach (var sc in p.SessionClips)
            {
                c.WriteInt((int)sc.FollowAction);
                c.WriteDouble(sc.LaunchQuantizeBeats);
            }

            c.WriteInt(p.Markers.Count);
            foreach (var m in p.Markers)
            {
                c.WriteGuid(m.Id);
                c.WriteString(m.Name);
                c.WriteDouble(m.Beat);
            }

            c.WriteInt(p.ArrangementSections.Count);
            foreach (var section in p.ArrangementSections)
            {
                c.WriteGuid(section.Id);
                c.WriteGuid(section.MarkerId);
            }

            c.WriteInt(p.SessionMidiMappings.Count);
            foreach (var m in p.SessionMidiMappings)
            {
                c.WriteInt((int)m.Action);
                c.WriteBool(m.IsNote);
                c.WriteInt(m.Channel);
                c.WriteInt(m.Number);
                c.WriteString(m.SourceDeviceId ?? "");
                c.WriteNullableGuid(m.TrackId);
                c.WriteBool(m.SceneIndex is not null);
                if (m.SceneIndex is { } si) c.WriteInt(si);
            }

            c.WriteInt(p.KeyRootPitchClass);
            c.WriteInt((int)p.KeyScale);

            c.WriteBool(p.VideoEnabled);
            c.WriteInt(p.VideoLayers.Count);
            foreach (var layer in p.VideoLayers)
            {
                c.WriteGuid(layer.Id);
                c.WriteString(layer.Name);
                c.WriteInt(layer.ZOrder);
                c.WriteDouble(layer.Opacity);
                c.WriteBool(layer.DefaultVisible);
                c.WriteDouble(layer.OffsetSeconds);
                c.WriteDouble(layer.InPointSeconds);
                c.WriteDouble(layer.OutPointSeconds);
                c.WriteDouble(layer.Fps);
                c.WriteBool(layer.Muted);
                c.WriteNullableGuid(layer.SyncClipId);
                c.WriteNullableGuid(layer.AudioSourceTrackId);
                c.WriteInt((int)layer.WaveformStyle);
                c.WriteBool(layer.WaveformFollowPlayhead);
                c.WriteInt(unchecked((int)layer.WaveformColorArgb));
                c.WriteDouble(layer.WaveformX);
                c.WriteDouble(layer.WaveformY);
                c.WriteDouble(layer.WaveformWidth);
                c.WriteDouble(layer.WaveformHeight);
                c.WriteInt((int)layer.VisualiserColorMode);
                c.WriteInt(unchecked((int)layer.VisualiserColorSecondaryArgb));
                c.WriteDouble(layer.SpectrumMinHz);
                c.WriteDouble(layer.SpectrumMaxHz);
                c.WriteDouble(layer.SpectrumLineThickness);
                c.WriteInt((int)layer.BlendMode);
                c.WriteDouble(layer.Scope3DCameraYaw);
                c.WriteDouble(layer.Scope3DCameraPitch);
                c.WriteDouble(layer.Scope3DCameraDistance);
                c.WriteDouble(layer.Scope3DLineThickness);
                c.WriteInt(layer.Scope3DTrailCount);
                c.WriteBool(layer.Scope3DTransparentBackground);
                c.WriteBool(layer.Engine3DEffectKind is not null);
                if (layer.Engine3DEffectKind is { } fxKind)
                    c.WriteInt((int)fxKind);
                c.WriteNullableGuid(layer.Engine3DAudioSourceTrackId);
                c.WriteString(layer.Engine3DImagePath ?? "");
                c.WriteDouble(layer.Engine3DX);
                c.WriteDouble(layer.Engine3DY);
                c.WriteDouble(layer.Engine3DWidth);
                c.WriteDouble(layer.Engine3DHeight);
                c.WriteDouble(layer.Engine3DCameraYaw);
                c.WriteDouble(layer.Engine3DCameraPitch);
                c.WriteDouble(layer.Engine3DCameraDistance);
                c.WriteInt(layer.Engine3DParticleCount);
                c.WriteDouble(layer.Engine3DParticleSize);
                c.WriteBool(layer.Engine3DTransparentBackground);
                c.WriteInt(unchecked((int)layer.Engine3DParticleColorArgb));
                c.WriteInt((int)layer.Engine3DParticleShape);
                c.WriteInt(layer.Items.Count);
                foreach (var item in layer.Items)
                {
                    c.WriteGuid(item.Id);
                    c.WriteInt((int)item.Kind);
                    c.WriteString(item.SourcePath);
                    c.WriteDouble(item.X);
                    c.WriteDouble(item.Y);
                    c.WriteDouble(item.Width);
                    c.WriteDouble(item.Height);
                    c.WriteDouble(item.Rotation);
                    c.WriteDouble(item.Opacity);
                    c.WriteString(item.TextContent);
                    c.WriteDouble(item.FontSizePx);
                    c.WriteInt(unchecked((int)item.TextColorArgb));
                    c.WriteNullableGuid(item.SubtitleClipId);
                    c.WriteString(item.SubtitleSrtPath ?? "");
                    c.WriteString(item.MaskImagePath ?? "");
                    c.WriteBool(item.ChromaKeyEnabled);
                    c.WriteInt(unchecked((int)item.ChromaKeyColorArgb));
                    c.WriteDouble(item.ChromaKeyTolerance);
                    c.WriteDouble(item.ChromaKeyFeather);
                    c.WriteDouble(item.Brightness);
                    c.WriteDouble(item.Contrast);
                    c.WriteDouble(item.Saturation);
                    c.WriteString(item.LutCubePath ?? "");
                }
            }

            c.WriteInt(p.VideoTriggers.Count);
            foreach (var tr in p.VideoTriggers)
            {
                c.WriteGuid(tr.Id);
                c.WriteGuid(tr.TargetLayerId);
                c.WriteInt((int)tr.Source);
                c.WriteNullableGuid(tr.TrackId);
                c.WriteNullableGuid(tr.ClipId);
                c.WriteBool(tr.MidiNote is not null);
                if (tr.MidiNote is { } note) c.WriteInt(note);
                c.WriteBool(tr.MidiCcChannel is not null);
                if (tr.MidiCcChannel is { } ccCh) c.WriteInt(ccCh);
                c.WriteBool(tr.MidiCcNumber is not null);
                if (tr.MidiCcNumber is { } ccNum) c.WriteInt(ccNum);
                c.WriteDouble(tr.MidiCcThreshold);
                c.WriteInt((int)tr.Moment);
                c.WriteInt((int)tr.Action);
                c.WriteDouble(tr.FadeDurationSeconds);
            }

            c.WriteInt(p.VideoCanvasWidth);
            c.WriteInt(p.VideoCanvasHeight);
            c.WriteDouble(p.VideoExportFps);

            c.WriteInt(p.VideoVisibilityRegions.Count);
            foreach (var region in p.VideoVisibilityRegions)
            {
                c.WriteGuid(region.Id);
                c.WriteGuid(region.LayerId);
                c.WriteDouble(region.StartBeat);
                c.WriteDouble(region.EndBeat);
                c.WriteDouble(region.FadeInBeats);
                c.WriteDouble(region.FadeOutBeats);
            }

            c.WriteInt(p.VideoLayerKeyframes.Count);
            foreach (var kf in p.VideoLayerKeyframes)
            {
                c.WriteGuid(kf.Id);
                c.WriteGuid(kf.ItemId);
                c.WriteDouble(kf.Beat);
                c.WriteDouble(kf.X);
                c.WriteDouble(kf.Y);
                c.WriteDouble(kf.Width);
                c.WriteDouble(kf.Height);
                c.WriteDouble(kf.Opacity);
            }

            // Project Clips sidebar organisation (v22+, trailing — older readers stop via ChunkHasMore).
            c.WriteInt((int)p.ProjectClipsSortMode);
            c.WriteInt(p.ProjectClipCategories.Count);
            foreach (var cat in p.ProjectClipCategories)
            {
                c.WriteGuid(cat.Id);
                c.WriteString(cat.Name);
                c.WriteInt(cat.ClipKeys.Count);
                foreach (var key in cat.ClipKeys) c.WriteString(key);
            }
        });
    }

    private static void WritePattern(OngenWriter w, Pattern pat)
    {
        w.WriteChunk(c =>
        {
            c.WriteGuid(pat.Id);
            c.WriteString(pat.Name);
            c.WriteDouble(pat.LengthBeats);
            c.WriteInt(pat.ColorIndex);
            c.WriteInt(pat.Channels.Count);
            foreach (var ch in pat.Channels)
            {
                c.WriteGuid(ch.Id);
                c.WriteInt(ch.Order);
                c.WriteInt((int)ch.SourceKind);
                c.WriteGuid(ch.TrackId);
                c.WriteNullableGuid(ch.SampleClipId);
                c.WriteString(ch.Name);
                c.WriteBool(ch.Muted);
                c.WriteDouble(ch.Volume);
                c.WriteDouble(ch.Pan);
            }

            c.WriteInt(pat.StepSequences.Count);
            foreach (var seq in pat.StepSequences) WriteStepSequence(c, seq);
        });
    }

    private static void WriteStepSequence(OngenWriter w, StepSequence seq)
    {
        w.WriteChunk(c =>
        {
            c.WriteGuid(seq.Id);
            c.WriteGuid(seq.PatternChannelId);
            c.WriteInt(seq.StepCount);
            c.WriteInt(seq.Steps.Count);
            foreach (var s in seq.Steps)
            {
                c.WriteBool(s.Active);
                c.WriteInt(s.Note);
                c.WriteFloat(s.Velocity);
                c.WriteFloat(s.Pan);
                c.WriteFloat(s.Probability);
                c.WriteInt(s.MicroTimingTicks);
            }
        });
    }

    private static void WriteDrumMap(OngenWriter w, DrumMap dm)
    {
        w.WriteChunk(c =>
        {
            c.WriteGuid(dm.Id);
            c.WriteString(dm.Name);
            c.WriteInt(dm.Entries.Count);
            foreach (var e in dm.Entries)
            {
                c.WriteInt(e.Note);
                c.WriteString(e.Label);
                c.WriteNullableGuid(e.SampleClipId);
                c.WriteFloat(e.VelocityScale);
            }
        });
    }

    private static void WriteMidiMappings(OngenWriter w, Project p)
    {
        w.WriteChunk(c =>
        {
            c.WriteInt(p.MidiMappings.Count);
            foreach (var m in p.MidiMappings)
            {
                c.WriteInt(p.Tracks.IndexOf(m.Owner)); // owner referenced by track index
                c.WriteInt(m.Channel);
                c.WriteInt(m.Controller);
                c.WriteInt((int)m.Binding.Kind);
                c.WriteInt(m.Binding.EffectIndex);
                c.WriteInt(m.Binding.ParamIndex);
            }
        });
    }

    private static void ReadMidiMappings(OngenReader c, Project project)
    {
        var count = c.ReadInt();
        for (var i = 0; i < count; i++)
        {
            var ownerIndex = c.ReadInt();
            var channel = c.ReadInt();
            var controller = c.ReadInt();
            var kind = c.ReadInt();
            var eff = c.ReadInt();
            var param = c.ReadInt();
            if (ownerIndex < 0 || ownerIndex >= project.Tracks.Count) continue;
            project.MidiMappings.Add(new MidiMapping
            {
                Owner = project.Tracks[ownerIndex],
                Channel = channel,
                Controller = controller,
                Binding = new AutomationBinding((AutomationTargetKind)kind, eff, param),
            });
        }
    }

    private static void WriteTrack(OngenWriter w, Track t, SampleStore store)
    {
        w.WriteChunk(c =>
        {
            c.WriteGuid(t.Id);
            c.WriteString(t.Name);
            c.WriteInt((int)t.Kind);
            c.WriteNullableGuid(t.ParentId);
            c.WriteBool(t.IsMuted);
            c.WriteBool(t.IsSoloed);
            c.WriteDouble(t.Volume);
            c.WriteDouble(t.Pan);
            c.WriteString(t.ColorKey);
            c.WriteBool(t.AutomationCollapsed);
            c.WriteBool(t.GroupCollapsed);

            // Instrument rack: a list of slots, each its own instrument + (pre) effect chain.
            c.WriteInt(t.Instruments.Count);
            foreach (var slot in t.Instruments)
            {
                var inst = slot.Instrument;
                ComponentSerializer.WriteComponent(c, inst.TypeId, inst, inst.Parameters, store, slot.Enabled, inst as ISampleHost);
                c.WriteInt(slot.Effects.Count);
                foreach (var e in slot.Effects) ComponentSerializer.WriteComponent(c, e.TypeId, e, e.Parameters, store, e.Enabled, e as ISampleHost);
                // v4 slot routing (trailing per slot when fileVersion >= 4 handled at read; always write for v4+)
                c.WriteInt(slot.OutputBusIndex);
                c.WriteNullableGuid(slot.OutputTrackId);
            }

            c.WriteInt(t.Effects.Count);
            foreach (var e in t.Effects) ComponentSerializer.WriteComponent(c, e.TypeId, e, e.Parameters, store, e.Enabled, e as ISampleHost);

            c.WriteInt(t.AutoLanes.Count);
            foreach (var lane in t.AutoLanes) WriteAutoLane(c, lane);

            c.WriteInt(t.Clips.Count);
            foreach (var clip in t.Clips) WriteClip(c, clip, store);

            // v3 routing (trailing — older readers stop after clips).
            c.WriteInt((int)t.OutputTarget);
            c.WriteNullableGuid(t.OutputBusId);
            c.WriteBool(t.RouteToMaster);
            c.WriteInt(t.Sends.Count);
            foreach (var send in t.Sends)
            {
                c.WriteGuid(send.Id);
                c.WriteGuid(send.TargetTrackId);
                c.WriteDouble(send.Level);
                c.WriteBool(send.PreFader);
                c.WriteBool(send.Enabled);
            }

            // v4 take lanes
            c.WriteInt(t.TakeLanes.Count);
            foreach (var lane in t.TakeLanes)
            {
                c.WriteGuid(lane.Id);
                c.WriteString(lane.Name);
                c.WriteBool(lane.IsExpanded);
                c.WriteInt(lane.Takes.Count);
                foreach (var take in lane.Takes)
                {
                    c.WriteGuid(take.Id);
                    c.WriteGuid(take.ClipId);
                    c.WriteBool(take.IsSelected);
                    c.WriteDouble(take.StartBeat);
                    c.WriteDouble(take.LengthBeats);
                }
                c.WriteDouble(lane.LaneHeight);
            }

            c.WriteDouble(t.SurroundWidth);
            c.WriteNullableGuid(t.ActivePatternId);
            c.WriteNullableGuid(t.DrumMapId);
            c.WriteBool(t.IsFrozen);
            c.WriteBool(t.RouteToExternalMidi);
            c.WriteInt(t.ExternalMidiChannel);
            c.WriteNullableGuid(t.ActiveTakeLaneId);

            c.WriteInt(t.Modulators.Count);
            foreach (var mod in t.Modulators) WriteModulator(c, mod);

            // v21 per-track row height
            c.WriteDouble(t.LaneHeight);

            // v23 MIDI FX chain + instrument rack settings
            c.WriteInt(t.MidiEffects.Count);
            foreach (var mfx in t.MidiEffects)
                MidiEffectSerializer.Write(c, mfx);
            WriteRackSettings(c, t.Rack);
        });
    }

    private static void WriteRackSettings(OngenWriter c, InstrumentRackSettings rack)
    {
        c.WriteInt((int)rack.Kind);
        c.WriteInt(rack.Macros.Count);
        foreach (var m in rack.Macros)
        {
            c.WriteString(m.Label);
            c.WriteString(m.TargetParameterId);
            c.WriteDouble(m.Value);
        }
        c.WriteInt(rack.DrumPads.Count);
        foreach (var p in rack.DrumPads)
        {
            c.WriteInt(p.PadIndex);
            c.WriteInt(p.MidiNote);
            c.WriteInt(p.InstrumentSlotIndex);
            c.WriteString(p.Label);
        }
    }

    private static void WriteModulator(OngenWriter c, TrackModulator mod)
    {
        c.WriteGuid(mod.Id);
        c.WriteInt((int)mod.Kind);
        c.WriteBool(mod.Enabled);
        c.WriteDouble(mod.RateHz);
        c.WriteDouble(mod.Depth);
        c.WriteInt((int)mod.Wave);
        c.WriteInt((int)mod.Target.Kind);
        c.WriteInt(mod.Target.EffectIndex);
        c.WriteInt(mod.Target.ParamIndex);
    }

    private static void WriteAutoLane(OngenWriter w, AutomationLane lane)
    {
        w.WriteChunk(c =>
        {
            var b = lane.Binding;
            c.WriteInt(b is null ? -1 : (int)b.Kind);
            c.WriteInt(b?.EffectIndex ?? -1);
            c.WriteInt(b?.ParamIndex ?? -1);
            c.WriteString(lane.Target.Name);
            c.WriteBool(lane.IsArmed);
            c.WriteInt(lane.Points.Count);
            foreach (var pt in lane.Points)
            {
                c.WriteDouble(pt.Beat);
                c.WriteDouble(pt.Value);
                c.WriteDouble(pt.Curve);
            }
            c.WriteDouble(lane.LaneHeight);
        });
    }

    private static void WriteClip(OngenWriter w, Clip clip, SampleStore store)
    {
        w.WriteChunk(c =>
        {
            c.WriteGuid(clip.Id);
            c.WriteString(clip.Name);
            c.WriteDouble(clip.StartBeat);
            c.WriteDouble(clip.LengthBeats);
            c.WriteBool(clip.IsAudio);
            c.WriteBool(clip.StretchToTempo);
            c.WriteNullableDouble(clip.SourceTempo);
            c.WriteDouble(clip.SourceOffsetSeconds);
            c.WriteNullableDouble(clip.SourceLengthSeconds);
            c.WriteString(clip.Samples is { } buf ? store.Add(buf) : "");
            c.WriteString(clip.AudioFilePath ?? "");
            c.WriteInt(clip.Notes.Count);
            foreach (var n in clip.Notes)
            {
                c.WriteInt(n.Note);
                c.WriteDouble(n.StartBeat);
                c.WriteDouble(n.LengthBeats);
                c.WriteFloat(n.Velocity);
            }

            // Appended after the notes so older readers (which stop here) load fine; newer readers pick it
            // up via ChunkHasMore.
            c.WriteBool(clip.PitchCorrected);
            c.WriteString(clip.SourceKey ?? "");

            // v4 warp markers
            c.WriteInt((int)clip.WarpMode);
            c.WriteInt(clip.WarpMarkers.Count);
            foreach (var wm in clip.WarpMarkers)
            {
                c.WriteDouble(wm.SourceSeconds);
                c.WriteDouble(wm.BeatPosition);
            }

            c.WriteDouble(clip.UserFadeInBeats);
            c.WriteDouble(clip.UserFadeOutBeats);
            c.WriteBool(clip.HasAraRegion);
            c.WriteDouble(clip.AraPitchOffsetSemitones);
            c.WriteNullableGuid(clip.LinkedClipGroupId);
            c.WriteInt(clip.PitchSegments.Count);
            foreach (var ps in clip.PitchSegments)
            {
                c.WriteLong(ps.StartSample);
                c.WriteLong(ps.EndSample);
                c.WriteDouble(ps.PitchCents);
                c.WriteFloat(ps.Amplitude);
            }

            c.WriteInt((int)clip.Origin);
            c.WriteNullableGuid(clip.CapturedFromSessionClipId);
        });
    }

    // ----------------------------------------------------------------- Load

    public static LoadResult Load(Stream input, IInstrumentRegistry instruments, IEffectRegistry effects,
        IMidiEffectRegistry? midiEffects = null)
    {
        midiEffects ??= new MidiEffectRegistry();
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var warnings = new List<string>();

        // Manifest / version sniff.
        var manifest = zip.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException("Not an Ongenet project file (missing manifest).");
        int fileVersion;
        using (var ms = ReadEntry(manifest))
        using (var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true))
        {
            var magic = br.ReadBytes(Magic.Length);
            if (!MagicMatches(magic)) throw new InvalidDataException("Not an Ongenet project file (bad header).");
            fileVersion = br.ReadInt32();
            // appVersion + ticks follow but aren't needed here.
        }

        var fromNewer = fileVersion > FormatVersion;
        if (fromNewer)
            warnings.Add($"This project was saved by a newer version of Ongenet (format v{fileVersion}). " +
                         "Anything this version doesn't understand was skipped.");

        var samples = new SampleLoader(zip, warnings);

        var projectEntry = zip.GetEntry(ProjectEntry)
            ?? throw new InvalidDataException("Not an Ongenet project file (missing project data).");

        var project = new Project();
        double loopStart = 0, loopEnd = 0, startBeat = 0;

        using (var ms = ReadEntry(projectEntry))
        using (var r = new OngenReader(ms))
        {
            r.ReadChunk(c =>
            {
                project.Name = c.ReadString();
                project.Tempo = new Tempo(c.ReadDouble());
                var num = c.ReadInt();
                var den = c.ReadInt();
                project.TimeSignature = new TimeSignature(num, den);
                project.BarCount = c.ReadInt();
                loopStart = c.ReadDouble();
                loopEnd = c.ReadDouble();
                startBeat = c.ReadDouble();
                if (c.ChunkHasMore)
                {
                    _ = c.ReadInt(); // legacy KeyRootPitchClass
                    if (c.ChunkHasMore) _ = c.ReadBool(); // legacy KeyIsMinor
                }
            });

            var trackCount = r.ReadInt();
            for (var i = 0; i < trackCount; i++)
                project.Tracks.Add(ReadTrack(r, instruments, effects, midiEffects, samples, warnings, fileVersion, project));

            // Optional trailing MIDI-mappings chunk (absent in files saved before this feature).
            if (r.HasMore) r.ReadChunk(c => ReadMidiMappings(c, project));

            if (r.HasMore) r.ReadChunk(c => ReadProjectExtensions(c, project, fileVersion));
        }

        return new LoadResult(project, loopStart, loopEnd, startBeat, warnings, fromNewer);
    }

    private static Track ReadTrack(OngenReader r, IInstrumentRegistry instruments, IEffectRegistry effects,
        IMidiEffectRegistry midiEffects, SampleLoader samples, List<string> warnings, int fileVersion, Project project)
    {
        Track track = null!;
        r.ReadChunk(c =>
        {
            track = new Track { Id = c.ReadGuid() };
            track.Name = c.ReadString();
            track.Kind = (TrackKind)c.ReadInt();
            track.ParentId = c.ReadNullableGuid();
            track.IsMuted = c.ReadBool();
            track.IsSoloed = c.ReadBool();
            track.Volume = c.ReadDouble();
            track.Pan = c.ReadDouble();
            track.ColorKey = c.ReadString();
            track.AutomationCollapsed = c.ReadBool();
            track.GroupCollapsed = c.ReadBool();

            // Instrument rack. v1 stored a single optional instrument (bool + component); v2+ stores a
            // count-prefixed list of slots, each an instrument followed by its own effect chain.
            if (fileVersion < 2)
            {
                if (c.ReadBool() && ComponentSerializer.ReadInstrument(c, instruments, effects, midiEffects, samples.Get, warnings).Instrument is { } legacy)
                    track.Instruments.Add(new InstrumentSlot(legacy) { Enabled = true });
            }
            else
            {
                var slotCount = c.ReadInt();
                for (var i = 0; i < slotCount; i++)
                {
                    var (inst, enabled) = ComponentSerializer.ReadInstrument(c, instruments, effects, midiEffects, samples.Get, warnings);
                    var fxCountSlot = c.ReadInt();
                    var slotFx = new List<IAudioEffect>();
                    for (var j = 0; j < fxCountSlot; j++)
                        if (ComponentSerializer.ReadEffect(c, instruments, effects, midiEffects, samples.Get, warnings) is { } sfx) slotFx.Add(sfx);

                    if (inst is null) continue; // instrument type unavailable; its effects are dropped
                    var slot = new InstrumentSlot(inst) { Enabled = enabled };
                    foreach (var sfx in slotFx) slot.Effects.Add(sfx);
                    slot.CommitEffects();
                    if (fileVersion >= 4)
                    {
                        slot.OutputBusIndex = c.ReadInt();
                        slot.OutputTrackId = c.ReadNullableGuid();
                    }
                    track.Instruments.Add(slot);
                }
            }

            // Effects
            var fxCount = c.ReadInt();
            for (var i = 0; i < fxCount; i++)
            {
                var fx = ComponentSerializer.ReadEffect(c, instruments, effects, midiEffects, samples.Get, warnings);
                if (fx is not null) track.Effects.Add(fx);
            }

            // Automation lanes (after instrument + effects so targets resolve)
            var laneCount = c.ReadInt();
            for (var i = 0; i < laneCount; i++)
            {
                var lane = ReadAutoLane(c, track, warnings, project);
                if (lane is not null) track.AutoLanes.Add(lane);
            }

            // Clips
            var clipCount = c.ReadInt();
            for (var i = 0; i < clipCount; i++)
                track.Clips.Add(ReadClip(c, samples));

            // v3 routing (optional trailing fields).
            if (fileVersion >= 3 && c.ChunkHasMore)
            {
                track.OutputTarget = (TrackOutputTarget)c.ReadInt();
                track.OutputBusId = c.ReadNullableGuid();
                track.RouteToMaster = c.ReadBool();
                var sendCount = c.ReadInt();
                for (var i = 0; i < sendCount; i++)
                {
                    track.Sends.Add(new TrackSend
                    {
                        Id = c.ReadGuid(),
                        TargetTrackId = c.ReadGuid(),
                        Level = c.ReadDouble(),
                        PreFader = c.ReadBool(),
                        Enabled = c.ReadBool()
                    });
                }
            }

            if (c.ChunkHasMore)
            {
                var takeLaneCount = c.ReadInt();
                for (var i = 0; i < takeLaneCount; i++)
                {
                    var lane = new TakeLane
                    {
                        Id = c.ReadGuid(),
                        Name = c.ReadString(),
                        IsExpanded = c.ReadBool()
                    };
                    var takeCount = c.ReadInt();
                    for (var j = 0; j < takeCount; j++)
                    {
                        lane.Takes.Add(new Take
                        {
                            Id = c.ReadGuid(),
                            ClipId = c.ReadGuid(),
                            IsSelected = c.ReadBool(),
                            StartBeat = c.ReadDouble(),
                            LengthBeats = c.ReadDouble()
                        });
                    }
                    if (fileVersion >= 21)
                        lane.LaneHeight = c.ReadDouble();
                    track.TakeLanes.Add(lane);
                }
            }

            if (c.ChunkHasMore)
                track.SurroundWidth = c.ReadDouble();
            if (c.ChunkHasMore)
                track.ActivePatternId = c.ReadNullableGuid();
            if (c.ChunkHasMore)
                track.DrumMapId = c.ReadNullableGuid();
            if (c.ChunkHasMore)
                track.IsFrozen = c.ReadBool();
            if (c.ChunkHasMore)
                track.RouteToExternalMidi = c.ReadBool();
            if (c.ChunkHasMore)
                track.ExternalMidiChannel = c.ReadInt();
            if (c.ChunkHasMore)
                track.ActiveTakeLaneId = c.ReadNullableGuid();
            if (c.ChunkHasMore)
            {
                var modCount = c.ReadInt();
                for (var i = 0; i < modCount; i++)
                    track.Modulators.Add(ReadModulator(c));
            }
            if (fileVersion >= 21 && c.ChunkHasMore)
                track.LaneHeight = c.ReadDouble();
            if (fileVersion >= 23 && c.ChunkHasMore)
            {
                var mfxCount = c.ReadInt();
                for (var i = 0; i < mfxCount; i++)
                    if (MidiEffectSerializer.Read(c, midiEffects, warnings) is { } mfx)
                        track.MidiEffects.Add(mfx);
                if (c.ChunkHasMore)
                    ReadRackSettings(c, track.Rack);
            }
        });

        // Populate the audio-thread snapshots the engine reads.
        track.CommitInstruments();
        track.CommitEffects();
        track.CommitMidiEffects();
        track.CommitAutoLanes();
        track.CommitModulators();
        return track;
    }

    private static void ReadRackSettings(OngenReader c, InstrumentRackSettings rack)
    {
        rack.Kind = (RackKind)c.ReadInt();
        rack.Macros.Clear();
        var macroCount = c.ReadInt();
        for (var i = 0; i < macroCount; i++)
        {
            rack.Macros.Add(new RackMacroKnob
            {
                Label = c.ReadString(),
                TargetParameterId = c.ReadString(),
                Value = c.ReadDouble()
            });
        }
        rack.DrumPads.Clear();
        var padCount = c.ReadInt();
        for (var i = 0; i < padCount; i++)
        {
            rack.DrumPads.Add(new DrumPadSlot
            {
                PadIndex = c.ReadInt(),
                MidiNote = c.ReadInt(),
                InstrumentSlotIndex = c.ReadInt(),
                Label = c.ReadString()
            });
        }
    }

    private static TrackModulator ReadModulator(OngenReader c)
        => new()
        {
            Id = c.ReadGuid(),
            Kind = (TrackModulatorKind)c.ReadInt(),
            Enabled = c.ReadBool(),
            RateHz = c.ReadDouble(),
            Depth = c.ReadDouble(),
            Wave = (LfoWave)c.ReadInt(),
            Target = new AutomationBinding((AutomationTargetKind)c.ReadInt(), c.ReadInt(), c.ReadInt())
        };

    private static AutomationLane? ReadAutoLane(OngenReader r, Track track, List<string> warnings, Project? project)
    {
        AutomationLane? lane = null;
        r.ReadChunk(c =>
        {
            var kind = c.ReadInt();
            var effectIndex = c.ReadInt();
            var paramIndex = c.ReadInt();
            var name = c.ReadString();
            var isArmed = c.ReadBool();
            var pointCount = c.ReadInt();
            var points = new List<AutomationPoint>(pointCount);
            for (var i = 0; i < pointCount; i++)
                points.Add(new AutomationPoint(c.ReadDouble(), c.ReadDouble(), c.ReadDouble()));

            var target = BuildTarget(track, kind, effectIndex, paramIndex, project);
            if (target is null)
            {
                warnings.Add($"Automation lane '{name}' could not be re-bound; it was skipped.");
                return;
            }

            lane = new AutomationLane(target)
            {
                IsArmed = isArmed,
                Binding = new AutomationBinding((AutomationTargetKind)kind, effectIndex, paramIndex)
            };
            foreach (var pt in points) lane.Points.Add(pt);
            lane.Sort();
            if (c.ChunkHasMore)
                lane.LaneHeight = c.ReadDouble();
        });
        return lane;
    }

    /// <summary>
    /// Reconstructs a runtime <see cref="IAutomationTarget"/> from a persisted binding (kind +
    /// effect/param indices) against <paramref name="track"/>. Public so MIDI-controller mappings can
    /// resolve their targets the same way automation lanes do on load. Returns null if it can't bind.
    /// </summary>
    public static IAutomationTarget? BuildTarget(Track track, int kind, int effectIndex, int paramIndex,
        Project? project = null)
    {
        switch ((AutomationTargetKind)kind)
        {
            case AutomationTargetKind.TrackVolume:
                return new DelegateAutomationTarget("Volume", 0, 1, () => track.Volume, v => track.Volume = v);
            case AutomationTargetKind.TrackPan:
                return new DelegateAutomationTarget("Pan", -1, 1, () => track.Pan, v => track.Pan = v);
            case AutomationTargetKind.TrackSendLevel:
                if (paramIndex < 0 || paramIndex >= track.Sends.Count) return null;
                var send = track.Sends[paramIndex];
                var sendTarget = project?.Tracks.FirstOrDefault(t => t.Id == send.TargetTrackId)?.Name ?? "Return";
                return new DelegateAutomationTarget($"Send {sendTarget}", 0, 1,
                    () => send.Level, v => send.Level = v);
            case AutomationTargetKind.Tempo:
                return project is null ? null : ProjectAutomationTargets.Tempo(project);
            case AutomationTargetKind.TimeSignature:
                return project is null ? null : ProjectAutomationTargets.TimeSignature(project);
            case AutomationTargetKind.EffectEnabled:
                if (effectIndex < 0 || effectIndex >= track.Effects.Count) return null;
                var fx = track.Effects[effectIndex];
                return new DelegateAutomationTarget($"{fx.Name} On/Off", 0, 1,
                    () => fx.Enabled ? 1 : 0, v => fx.Enabled = v >= 0.5, stepped: true);
            case AutomationTargetKind.EffectParam:
                if (effectIndex < 0 || effectIndex >= track.Effects.Count) return null;
                return FromParameter(track.Effects[effectIndex].Parameters, paramIndex);
            case AutomationTargetKind.InstrumentParam:
                // effectIndex carries the rack slot index (v1 files used -1 for the single instrument → slot 0).
                var slot = effectIndex < 0 ? 0 : effectIndex;
                if (slot >= track.Instruments.Count) return null;
                return FromParameter(track.Instruments[slot].Instrument.Parameters, paramIndex);
            default:
                return null;
        }
    }

    private static IAutomationTarget? FromParameter(IReadOnlyList<Parameter> parameters, int index)
    {
        if (index < 0 || index >= parameters.Count) return null;
        switch (parameters[index])
        {
            case FloatParameter f:
                return new DelegateAutomationTarget(f.Name, f.Min, f.Max, () => f.Value, v => f.Value = v);
            case BoolParameter b:
                return new DelegateAutomationTarget(b.Name, 0, 1, () => b.Value ? 1 : 0, v => b.Value = v >= 0.5, stepped: true);
            case ChoiceParameter ch:
                return new DelegateAutomationTarget(ch.Name, 0, Math.Max(0, ch.Options.Count - 1),
                    () => ch.SelectedIndex, v => ch.SelectedIndex = (int)Math.Round(v), stepped: true);
            default:
                return null;
        }
    }

    private static Clip ReadClip(OngenReader r, SampleLoader samples)
    {
        var clip = new Clip();
        r.ReadChunk(c =>
        {
            clip = new Clip { Id = c.ReadGuid() };
            clip.Name = c.ReadString();
            clip.StartBeat = c.ReadDouble();
            clip.LengthBeats = c.ReadDouble();
            clip.IsAudio = c.ReadBool();
            clip.StretchToTempo = c.ReadBool();
            clip.SourceTempo = c.ReadNullableDouble();
            clip.SourceOffsetSeconds = c.ReadDouble();
            clip.SourceLengthSeconds = c.ReadNullableDouble();
            var sampleRef = c.ReadString();
            clip.AudioFilePath = c.ReadString() is { Length: > 0 } path ? path : null;
            var noteCount = c.ReadInt();
            for (var i = 0; i < noteCount; i++)
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = c.ReadInt(),
                    StartBeat = c.ReadDouble(),
                    LengthBeats = c.ReadDouble(),
                    Velocity = c.ReadFloat()
                });
            }

            if (sampleRef.Length > 0 && samples.Get(sampleRef) is { } buf)
            {
                clip.Samples = buf;
                clip.Waveform = AudioWaveform.Build(buf);
            }

            // Trailing field added in a later format revision; absent in older files.
            if (c.ChunkHasMore) clip.PitchCorrected = c.ReadBool();
            if (c.ChunkHasMore)
            {
                var key = c.ReadString();
                clip.SourceKey = key.Length > 0 ? key : null;
            }

            if (c.ChunkHasMore)
            {
                clip.WarpMode = (WarpMode)c.ReadInt();
                var wmCount = c.ReadInt();
                for (var i = 0; i < wmCount; i++)
                {
                    clip.WarpMarkers.Add(new WarpMarker
                    {
                        SourceSeconds = c.ReadDouble(),
                        BeatPosition = c.ReadDouble()
                    });
                }
            }

            if (c.ChunkHasMore)
            {
                clip.UserFadeInBeats = c.ReadDouble();
                clip.UserFadeOutBeats = c.ReadDouble();
            }

            if (c.ChunkHasMore) clip.HasAraRegion = c.ReadBool();
            if (c.ChunkHasMore) clip.AraPitchOffsetSemitones = c.ReadDouble();
            if (c.ChunkHasMore) clip.LinkedClipGroupId = c.ReadNullableGuid();
            if (c.ChunkHasMore)
            {
                var segCount = c.ReadInt();
                for (var i = 0; i < segCount; i++)
                {
                    clip.PitchSegments.Add(new PitchNoteSegment
                    {
                        StartSample = c.ReadLong(),
                        EndSample = c.ReadLong(),
                        PitchCents = c.ReadDouble(),
                        Amplitude = c.ReadFloat()
                    });
                }
            }

            if (c.ChunkHasMore) clip.Origin = (ClipOrigin)c.ReadInt();
            if (c.ChunkHasMore) clip.CapturedFromSessionClipId = c.ReadNullableGuid();
        });
        return clip;
    }

    private static void ReadProjectExtensions(OngenReader c, Project project, int fileVersion)
    {
        var patCount = c.ReadInt();
        for (var i = 0; i < patCount; i++)
        {
            Pattern? pat = null;
            c.ReadChunk(pc =>
            {
                pat = new Pattern { Id = pc.ReadGuid() };
                pat.Name = pc.ReadString();
                pat.LengthBeats = pc.ReadDouble();
                pat.ColorIndex = pc.ReadInt();
                var chCount = pc.ReadInt();
                for (var j = 0; j < chCount; j++)
                {
                    var chId = pc.ReadGuid();
                    PatternChannel ch;
                    if (fileVersion >= 5)
                    {
                        ch = new PatternChannel
                        {
                            Id = chId,
                            Order = pc.ReadInt(),
                            SourceKind = (PatternRowSourceKind)pc.ReadInt(),
                            TrackId = pc.ReadGuid(),
                            SampleClipId = pc.ReadNullableGuid(),
                            Name = pc.ReadString(),
                            Muted = pc.ReadBool(),
                            Volume = pc.ReadDouble(),
                            Pan = pc.ReadDouble()
                        };
                    }
                    else
                    {
                        ch = new PatternChannel
                        {
                            Id = chId,
                            Order = j,
                            TrackId = pc.ReadGuid(),
                            Name = pc.ReadString(),
                            Muted = pc.ReadBool(),
                            Volume = pc.ReadDouble(),
                            Pan = pc.ReadDouble()
                        };
                    }
                    pat.Channels.Add(ch);
                }
                var seqCount = pc.ReadInt();
                for (var j = 0; j < seqCount; j++)
                    pat.StepSequences.Add(ReadStepSequence(pc));
            });
            if (pat is not null) project.Patterns.Add(pat);
        }

        var pcCount = c.ReadInt();
        for (var i = 0; i < pcCount; i++)
        {
            project.PatternClips.Add(new PatternClip
            {
                Id = c.ReadGuid(),
                PatternId = c.ReadGuid(),
                TrackId = c.ReadGuid(),
                StartBeat = c.ReadDouble(),
                LengthBeats = c.ReadDouble()
            });
        }

        var scCount = c.ReadInt();
        for (var i = 0; i < scCount; i++)
        {
            project.SessionClips.Add(new SessionClip
            {
                Id = c.ReadGuid(),
                TrackId = c.ReadGuid(),
                SceneIndex = c.ReadInt(),
                Name = c.ReadString(),
                LengthBeats = c.ReadDouble(),
                LaunchMode = (SessionLaunchMode)c.ReadInt(),
                SourceClipId = c.ReadNullableGuid()
            });
        }

        var moCount = c.ReadInt();
        for (var i = 0; i < moCount; i++)
        {
            project.MultiOutputRoutes.Add(new MultiOutputRoute
            {
                SourceTrackId = c.ReadGuid(),
                SlotIndex = c.ReadInt(),
                PluginOutputBus = c.ReadInt(),
                DestinationTrackId = c.ReadGuid(),
                Level = c.ReadDouble()
            });
        }

        project.Mpe.Enabled = c.ReadBool();
        project.Mpe.MasterChannel = c.ReadInt();
        project.Mpe.MemberChannelStart = c.ReadInt();
        project.Mpe.MemberChannelCount = c.ReadInt();

        if (c.ReadBool())
        {
            project.ActiveGroove = new GrooveTemplate
            {
                Id = c.ReadGuid(),
                Name = c.ReadString(),
                SwingAmount = c.ReadDouble(),
                Division = c.ReadInt()
            };
        }

        var dmCount = c.ReadInt();
        for (var i = 0; i < dmCount; i++)
        {
            DrumMap? dm = null;
            c.ReadChunk(dc =>
            {
                dm = new DrumMap { Id = dc.ReadGuid(), Name = dc.ReadString() };
                var ec = dc.ReadInt();
                for (var j = 0; j < ec; j++)
                {
                    dm.Entries.Add(new DrumMapEntry
                    {
                        Note = dc.ReadInt(),
                        Label = dc.ReadString(),
                        SampleClipId = dc.ReadNullableGuid(),
                        VelocityScale = dc.ReadFloat()
                    });
                }
            });
            if (dm is not null) project.DrumMaps.Add(dm);
        }

        var legacyVideoTrackCount = 0;
        if (c.ChunkHasMore)
        {
            var vtCount = c.ReadInt();
            for (var i = 0; i < vtCount; i++)
            {
                var id = c.ReadGuid();
                var filePath = c.ReadString();
                var offsetSeconds = c.ReadDouble();
                var fps = c.ReadDouble();
                var muted = c.ReadBool();
                var inPoint = 0.0;
                var outPoint = 0.0;
                Guid? syncClipId = null;
                if (fileVersion >= 8)
                {
                    inPoint = c.ReadDouble();
                    outPoint = c.ReadDouble();
                    syncClipId = c.ReadNullableGuid();
                }

                if (fileVersion < 11)
                {
                    project.VideoLayers.Add(VideoLayerMigration.FromLegacyTrack(
                        id, filePath, offsetSeconds, fps, muted, inPoint, outPoint, syncClipId, i));
                    legacyVideoTrackCount++;
                }
            }
        }

        if (c.ChunkHasMore)
        {
            project.PlaybackMode = (PlaybackMode)c.ReadInt();
            project.LaunchQuantizeBeats = c.ReadDouble();
        }

        if (c.ChunkHasMore)
        {
            var faCount = c.ReadInt();
            for (var i = 0; i < faCount && i < project.SessionClips.Count; i++)
            {
                project.SessionClips[i].FollowAction = (FollowAction)c.ReadInt();
                project.SessionClips[i].LaunchQuantizeBeats = c.ReadDouble();
            }
        }

        if (c.ChunkHasMore)
        {
            var markerCount = c.ReadInt();
            for (var i = 0; i < markerCount; i++)
            {
                project.Markers.Add(new ArrangementMarker
                {
                    Id = c.ReadGuid(),
                    Name = c.ReadString(),
                    Beat = c.ReadDouble()
                });
            }
        }

        if (c.ChunkHasMore)
        {
            var sectionCount = c.ReadInt();
            for (var i = 0; i < sectionCount; i++)
            {
                project.ArrangementSections.Add(new ArrangementSection
                {
                    Id = c.ReadGuid(),
                    MarkerId = c.ReadGuid()
                });
            }
        }

        if (c.ChunkHasMore)
        {
            var smCount = c.ReadInt();
            for (var i = 0; i < smCount; i++)
            {
                project.SessionMidiMappings.Add(new SessionMidiMapping
                {
                    Action = (SessionMidiAction)c.ReadInt(),
                    IsNote = c.ReadBool(),
                    Channel = c.ReadInt(),
                    Number = c.ReadInt(),
                    SourceDeviceId = c.ReadString() is { Length: > 0 } sid ? sid : null,
                    TrackId = c.ReadNullableGuid(),
                    SceneIndex = c.ReadBool() ? c.ReadInt() : null
                });
            }
        }

        if (c.ChunkHasMore)
        {
            project.KeyRootPitchClass = c.ReadInt();
            project.KeyScale = (ScaleType)c.ReadInt();
            if (fileVersion >= 8)
            {
                project.VideoEnabled = c.ReadBool();
                var elCount = c.ReadInt();
                for (var i = 0; i < elCount; i++)
                {
                    if (fileVersion >= 12)
                        ReadVideoLayer(c, project, fileVersion);
                    else if (fileVersion >= 11)
                        ReadVideoLayerV11(c, project);
                    else if (fileVersion >= 10)
                        ReadVideoLayerLegacyV10(c, project, legacyVideoTrackCount);
                    else
                        ReadVideoLayerLegacyV8(c, project, legacyVideoTrackCount);
                }

                var trCount = c.ReadInt();
                for (var i = 0; i < trCount; i++)
                {
                    var trigger = new Models.Media.VideoTrigger
                    {
                        Id = c.ReadGuid(),
                        TargetLayerId = c.ReadGuid(),
                        Source = (Models.Media.VideoTriggerSource)c.ReadInt(),
                        TrackId = c.ReadNullableGuid(),
                        ClipId = c.ReadNullableGuid(),
                        MidiNote = c.ReadBool() ? c.ReadInt() : null
                    };
                    if (fileVersion >= 17)
                    {
                        trigger.MidiCcChannel = c.ReadBool() ? c.ReadInt() : null;
                        trigger.MidiCcNumber = c.ReadBool() ? c.ReadInt() : null;
                        trigger.MidiCcThreshold = c.ReadDouble();
                    }

                    trigger.Moment = (Models.Media.VideoTriggerMoment)c.ReadInt();
                    trigger.Action = (Models.Media.VideoTriggerAction)c.ReadInt();
                    trigger.FadeDurationSeconds = c.ReadDouble();
                    project.VideoTriggers.Add(trigger);
                }

                if (fileVersion >= 9)
                {
                    project.VideoCanvasWidth = c.ReadInt();
                    project.VideoCanvasHeight = c.ReadInt();
                }

                if (fileVersion >= 15)
                    project.VideoExportFps = c.ReadDouble();

                if (fileVersion >= 10)
                {
                    var regionCount = c.ReadInt();
                    for (var i = 0; i < regionCount; i++)
                    {
                        var region = new Models.Media.VideoVisibilityRegion
                        {
                            Id = c.ReadGuid(),
                            LayerId = c.ReadGuid(),
                            StartBeat = c.ReadDouble(),
                            EndBeat = c.ReadDouble()
                        };
                        if (fileVersion >= 16)
                        {
                            region.FadeInBeats = c.ReadDouble();
                            region.FadeOutBeats = c.ReadDouble();
                        }

                        project.VideoVisibilityRegions.Add(region);
                    }
                }

                if (fileVersion >= 18)
                {
                    var kfCount = c.ReadInt();
                    for (var i = 0; i < kfCount; i++)
                    {
                        project.VideoLayerKeyframes.Add(new Models.Media.VideoLayerKeyframe
                        {
                            Id = c.ReadGuid(),
                            ItemId = c.ReadGuid(),
                            Beat = c.ReadDouble(),
                            X = c.ReadDouble(),
                            Y = c.ReadDouble(),
                            Width = c.ReadDouble(),
                            Height = c.ReadDouble(),
                            Opacity = c.ReadDouble()
                        });
                    }
                }
            }
        }

        if (c.ChunkHasMore)
        {
            project.ProjectClipsSortMode = (ProjectClipsSortMode)c.ReadInt();
            var catCount = c.ReadInt();
            for (var i = 0; i < catCount; i++)
            {
                var cat = new ProjectClipCategory
                {
                    Id = c.ReadGuid(),
                    Name = c.ReadString()
                };
                var keyCount = c.ReadInt();
                for (var k = 0; k < keyCount; k++)
                    cat.ClipKeys.Add(c.ReadString());
                project.ProjectClipCategories.Add(cat);
            }
        }
    }

    private static void ReadVideoLayerLegacyV8(OngenReader c, Project project, int zOffset)
    {
        var id = c.ReadGuid();
        var name = c.ReadString();
        var kind = (Models.Media.VideoElementKind)c.ReadInt();
        var sourcePath = c.ReadString();
        var x = c.ReadDouble();
        var y = c.ReadDouble();
        var width = c.ReadDouble();
        var height = c.ReadDouble();
        var rotation = c.ReadDouble();
        var zOrder = c.ReadInt();
        var opacity = c.ReadDouble();
        var defaultVisible = c.ReadBool();
        var audioSourceTrackId = c.ReadNullableGuid();
        var waveformStyle = (Models.Media.VideoWaveformStyle)c.ReadInt();
        var waveformFollow = c.ReadBool();

        var layer = new Models.Media.VideoLayer
        {
            Id = id,
            Name = name,
            ZOrder = zOffset + zOrder,
            Opacity = opacity,
            DefaultVisible = defaultVisible,
            AudioSourceTrackId = audioSourceTrackId,
            WaveformStyle = waveformStyle,
            WaveformFollowPlayhead = waveformFollow
        };
        if (kind != Models.Media.VideoElementKind.Waveform)
        {
            layer.Items.Add(new Models.Media.VideoLayerItem
            {
                Kind = kind,
                SourcePath = sourcePath,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Rotation = rotation,
                Opacity = 1
            });
        }

        project.VideoLayers.Add(layer);
    }

    private static void ReadVideoLayerLegacyV10(OngenReader c, Project project, int zOffset)
    {
        var layer = new Models.Media.VideoLayer
        {
            Id = c.ReadGuid(),
            Name = c.ReadString(),
            ZOrder = zOffset + c.ReadInt(),
            Opacity = c.ReadDouble(),
            DefaultVisible = c.ReadBool(),
            AudioSourceTrackId = c.ReadNullableGuid(),
            WaveformStyle = (Models.Media.VideoWaveformStyle)c.ReadInt(),
            WaveformFollowPlayhead = c.ReadBool()
        };
        var itemCount = c.ReadInt();
        for (var j = 0; j < itemCount; j++)
        {
            layer.Items.Add(new Models.Media.VideoLayerItem
            {
                Id = c.ReadGuid(),
                Kind = (Models.Media.VideoElementKind)c.ReadInt(),
                SourcePath = c.ReadString(),
                X = c.ReadDouble(),
                Y = c.ReadDouble(),
                Width = c.ReadDouble(),
                Height = c.ReadDouble(),
                Rotation = c.ReadDouble(),
                Opacity = c.ReadDouble()
            });
        }

        project.VideoLayers.Add(layer);
    }

    private static void ReadVideoLayerV11(OngenReader c, Project project)
    {
        var layer = new Models.Media.VideoLayer
        {
            Id = c.ReadGuid(),
            Name = c.ReadString(),
            ZOrder = c.ReadInt(),
            Opacity = c.ReadDouble(),
            DefaultVisible = c.ReadBool(),
            OffsetSeconds = c.ReadDouble(),
            InPointSeconds = c.ReadDouble(),
            OutPointSeconds = c.ReadDouble(),
            Fps = c.ReadDouble(),
            Muted = c.ReadBool(),
            SyncClipId = c.ReadNullableGuid(),
            AudioSourceTrackId = c.ReadNullableGuid(),
            WaveformStyle = (Models.Media.VideoWaveformStyle)c.ReadInt(),
            WaveformFollowPlayhead = c.ReadBool()
        };
        var itemCount = c.ReadInt();
        for (var j = 0; j < itemCount; j++)
        {
            layer.Items.Add(new Models.Media.VideoLayerItem
            {
                Id = c.ReadGuid(),
                Kind = (Models.Media.VideoElementKind)c.ReadInt(),
                SourcePath = c.ReadString(),
                X = c.ReadDouble(),
                Y = c.ReadDouble(),
                Width = c.ReadDouble(),
                Height = c.ReadDouble(),
                Rotation = c.ReadDouble(),
                Opacity = c.ReadDouble()
            });
        }

        project.VideoLayers.Add(layer);
    }

    private static void ReadVideoLayer(OngenReader c, Project project, int fileVersion)
    {
        var layer = new Models.Media.VideoLayer
        {
            Id = c.ReadGuid(),
            Name = c.ReadString(),
            ZOrder = c.ReadInt(),
            Opacity = c.ReadDouble(),
            DefaultVisible = c.ReadBool(),
            OffsetSeconds = c.ReadDouble(),
            InPointSeconds = c.ReadDouble(),
            OutPointSeconds = c.ReadDouble(),
            Fps = c.ReadDouble(),
            Muted = c.ReadBool(),
            SyncClipId = c.ReadNullableGuid(),
            AudioSourceTrackId = c.ReadNullableGuid(),
            WaveformStyle = (Models.Media.VideoWaveformStyle)c.ReadInt(),
            WaveformFollowPlayhead = c.ReadBool(),
            WaveformColorArgb = unchecked((uint)c.ReadInt()),
            WaveformX = c.ReadDouble(),
            WaveformY = c.ReadDouble(),
            WaveformWidth = c.ReadDouble(),
            WaveformHeight = c.ReadDouble()
        };
        if (fileVersion >= 13)
        {
            layer.VisualiserColorMode = (Models.Media.VideoVisualiserColorMode)c.ReadInt();
            layer.VisualiserColorSecondaryArgb = unchecked((uint)c.ReadInt());
            layer.SpectrumMinHz = c.ReadDouble();
            layer.SpectrumMaxHz = c.ReadDouble();
            layer.SpectrumLineThickness = c.ReadDouble();
        }

        if (fileVersion >= 16)
            layer.BlendMode = (Models.Media.VideoBlendMode)c.ReadInt();

        if (fileVersion >= 19)
        {
            layer.Scope3DCameraYaw = c.ReadDouble();
            layer.Scope3DCameraPitch = c.ReadDouble();
            layer.Scope3DCameraDistance = c.ReadDouble();
            layer.Scope3DLineThickness = c.ReadDouble();
            layer.Scope3DTrailCount = c.ReadInt();
            layer.Scope3DTransparentBackground = c.ReadBool();
            if (c.ReadBool())
                layer.Engine3DEffectKind = (Models.Media.VideoEngine3DEffectKind)c.ReadInt();
            layer.Engine3DAudioSourceTrackId = c.ReadNullableGuid();
            layer.Engine3DImagePath = c.ReadString() is { Length: > 0 } img ? img : null;
            layer.Engine3DX = c.ReadDouble();
            layer.Engine3DY = c.ReadDouble();
            layer.Engine3DWidth = c.ReadDouble();
            layer.Engine3DHeight = c.ReadDouble();
            layer.Engine3DCameraYaw = c.ReadDouble();
            layer.Engine3DCameraPitch = c.ReadDouble();
            layer.Engine3DCameraDistance = c.ReadDouble();
            layer.Engine3DParticleCount = c.ReadInt();
            layer.Engine3DParticleSize = c.ReadDouble();
            layer.Engine3DTransparentBackground = c.ReadBool();
        }

        if (fileVersion >= 20)
        {
            layer.Engine3DParticleColorArgb = unchecked((uint)c.ReadInt());
            layer.Engine3DParticleShape = (Models.Media.VideoEngine3DParticleShape)c.ReadInt();
        }

        var itemCount = c.ReadInt();
        for (var j = 0; j < itemCount; j++)
        {
            var item = new Models.Media.VideoLayerItem
            {
                Id = c.ReadGuid(),
                Kind = (Models.Media.VideoElementKind)c.ReadInt(),
                SourcePath = c.ReadString(),
                X = c.ReadDouble(),
                Y = c.ReadDouble(),
                Width = c.ReadDouble(),
                Height = c.ReadDouble(),
                Rotation = c.ReadDouble(),
                Opacity = c.ReadDouble()
            };
            if (fileVersion >= 14)
            {
                item.TextContent = c.ReadString();
                item.FontSizePx = c.ReadDouble();
                item.TextColorArgb = unchecked((uint)c.ReadInt());
            }

            if (fileVersion >= 17)
            {
                item.SubtitleClipId = c.ReadNullableGuid();
                item.SubtitleSrtPath = c.ReadString() is { Length: > 0 } srt ? srt : null;
                item.MaskImagePath = c.ReadString() is { Length: > 0 } mask ? mask : null;
                item.ChromaKeyEnabled = c.ReadBool();
                item.ChromaKeyColorArgb = unchecked((uint)c.ReadInt());
                item.ChromaKeyTolerance = c.ReadDouble();
                item.ChromaKeyFeather = c.ReadDouble();
                item.Brightness = c.ReadDouble();
                item.Contrast = c.ReadDouble();
                item.Saturation = c.ReadDouble();
                item.LutCubePath = c.ReadString() is { Length: > 0 } lut ? lut : null;
            }

            layer.Items.Add(item);
        }

        project.VideoLayers.Add(layer);
    }

    private static StepSequence ReadStepSequence(OngenReader c)
    {
        StepSequence? seq = null;
        c.ReadChunk(sc =>
        {
            seq = new StepSequence
            {
                Id = sc.ReadGuid(),
                PatternChannelId = sc.ReadGuid(),
                StepCount = sc.ReadInt()
            };
            var stepCount = sc.ReadInt();
            for (var i = 0; i < stepCount; i++)
            {
                seq.Steps.Add(new StepData
                {
                    Active = sc.ReadBool(),
                    Note = sc.ReadInt(),
                    Velocity = sc.ReadFloat(),
                    Pan = sc.ReadFloat(),
                    Probability = sc.ReadFloat(),
                    MicroTimingTicks = sc.ReadInt()
                });
            }
        });
        return seq ?? new StepSequence();
    }

    private static bool MagicMatches(byte[] read)
    {
        if (read.Length != Magic.Length) return false;
        for (var i = 0; i < Magic.Length; i++)
            if (read[i] != Magic[i]) return false;
        return true;
    }

    private static MemoryStream ReadEntry(ZipArchiveEntry entry)
    {
        var ms = new MemoryStream();
        using (var s = entry.Open()) s.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    // ----------------------------------------------------------------- sample helpers

    // Parses embedded sample WAVs on demand, caching by hash so a shared sample becomes one in-memory buffer.
    private sealed class SampleLoader
    {
        private readonly ZipArchive _zip;
        private readonly List<string> _warnings;
        private readonly Dictionary<string, AudioSampleBuffer?> _cache = new();

        public SampleLoader(ZipArchive zip, List<string> warnings)
        {
            _zip = zip;
            _warnings = warnings;
        }

        public AudioSampleBuffer? Get(string hash)
        {
            if (_cache.TryGetValue(hash, out var cached)) return cached;

            var entry = _zip.GetEntry($"samples/{hash}.wav");
            AudioSampleBuffer? buffer = null;
            if (entry is null)
            {
                _warnings.Add("A clip's audio sample is missing from the project file.");
            }
            else
            {
                try
                {
                    using var ms = ReadEntry(entry);
                    buffer = WavParser.Parse(ms);
                }
                catch
                {
                    _warnings.Add("A clip's audio sample could not be read.");
                }
            }

            _cache[hash] = buffer;
            return buffer;
        }
    }
}
