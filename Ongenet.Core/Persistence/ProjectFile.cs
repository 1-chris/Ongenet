using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Models.Audio;

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
    /// <remarks>v2: instrument tracks store an instrument rack (a list of slots, each with its own effect
    /// chain) instead of a single optional instrument. v1 files load as a one-slot rack.</remarks>
    public const int FormatVersion = 2;

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
                foreach (var e in slot.Effects) ComponentSerializer.WriteComponent(c, e.TypeId, e, e.Parameters, store, e.Enabled, null);
            }

            c.WriteInt(t.Effects.Count);
            foreach (var e in t.Effects) ComponentSerializer.WriteComponent(c, e.TypeId, e, e.Parameters, store, e.Enabled, null);

            c.WriteInt(t.AutoLanes.Count);
            foreach (var lane in t.AutoLanes) WriteAutoLane(c, lane);

            c.WriteInt(t.Clips.Count);
            foreach (var clip in t.Clips) WriteClip(c, clip, store);
        });
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
        });
    }

    // ----------------------------------------------------------------- Load

    public static LoadResult Load(Stream input, IInstrumentRegistry instruments, IEffectRegistry effects)
    {
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
            });

            var trackCount = r.ReadInt();
            for (var i = 0; i < trackCount; i++)
                project.Tracks.Add(ReadTrack(r, instruments, effects, samples, warnings, fileVersion));

            // Optional trailing MIDI-mappings chunk (absent in files saved before this feature).
            if (r.HasMore) r.ReadChunk(c => ReadMidiMappings(c, project));
        }

        return new LoadResult(project, loopStart, loopEnd, startBeat, warnings, fromNewer);
    }

    private static Track ReadTrack(OngenReader r, IInstrumentRegistry instruments, IEffectRegistry effects,
        SampleLoader samples, List<string> warnings, int fileVersion)
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
                if (c.ReadBool() && ComponentSerializer.ReadInstrument(c, instruments, samples.Get, warnings).Instrument is { } legacy)
                    track.Instruments.Add(new InstrumentSlot(legacy) { Enabled = true });
            }
            else
            {
                var slotCount = c.ReadInt();
                for (var i = 0; i < slotCount; i++)
                {
                    var (inst, enabled) = ComponentSerializer.ReadInstrument(c, instruments, samples.Get, warnings);
                    var fxCountSlot = c.ReadInt();
                    var slotFx = new List<IAudioEffect>();
                    for (var j = 0; j < fxCountSlot; j++)
                        if (ComponentSerializer.ReadEffect(c, effects, warnings) is { } sfx) slotFx.Add(sfx);

                    if (inst is null) continue; // instrument type unavailable; its effects are dropped
                    var slot = new InstrumentSlot(inst) { Enabled = enabled };
                    foreach (var sfx in slotFx) slot.Effects.Add(sfx);
                    slot.CommitEffects();
                    track.Instruments.Add(slot);
                }
            }

            // Effects
            var fxCount = c.ReadInt();
            for (var i = 0; i < fxCount; i++)
            {
                var fx = ComponentSerializer.ReadEffect(c, effects, warnings);
                if (fx is not null) track.Effects.Add(fx);
            }

            // Automation lanes (after instrument + effects so targets resolve)
            var laneCount = c.ReadInt();
            for (var i = 0; i < laneCount; i++)
            {
                var lane = ReadAutoLane(c, track, warnings);
                if (lane is not null) track.AutoLanes.Add(lane);
            }

            // Clips
            var clipCount = c.ReadInt();
            for (var i = 0; i < clipCount; i++)
                track.Clips.Add(ReadClip(c, samples));
        });

        // Populate the audio-thread snapshots the engine reads.
        track.CommitInstruments();
        track.CommitEffects();
        track.CommitAutoLanes();
        return track;
    }

    private static AutomationLane? ReadAutoLane(OngenReader r, Track track, List<string> warnings)
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

            var target = BuildTarget(track, kind, effectIndex, paramIndex);
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
        });
        return lane;
    }

    /// <summary>
    /// Reconstructs a runtime <see cref="IAutomationTarget"/> from a persisted binding (kind +
    /// effect/param indices) against <paramref name="track"/>. Public so MIDI-controller mappings can
    /// resolve their targets the same way automation lanes do on load. Returns null if it can't bind.
    /// </summary>
    public static IAutomationTarget? BuildTarget(Track track, int kind, int effectIndex, int paramIndex)
    {
        switch ((AutomationTargetKind)kind)
        {
            case AutomationTargetKind.TrackVolume:
                return new DelegateAutomationTarget("Volume", 0, 1, () => track.Volume, v => track.Volume = v);
            case AutomationTargetKind.TrackPan:
                return new DelegateAutomationTarget("Pan", -1, 1, () => track.Pan, v => track.Pan = v);
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
        });
        return clip;
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
