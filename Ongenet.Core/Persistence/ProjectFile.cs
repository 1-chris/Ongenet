using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
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
    public const int FormatVersion = 1;

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

            c.WriteBool(t.Instrument is not null);
            if (t.Instrument is not null) WriteComponent(c, t.Instrument.TypeId, t.Instrument, t.Instrument.Parameters, store, false, t.Instrument as ISampleHost);

            c.WriteInt(t.Effects.Count);
            foreach (var e in t.Effects) WriteComponent(c, e.TypeId, e, e.Parameters, store, e.Enabled, null);

            c.WriteInt(t.AutoLanes.Count);
            foreach (var lane in t.AutoLanes) WriteAutoLane(c, lane);

            c.WriteInt(t.Clips.Count);
            foreach (var clip in t.Clips) WriteClip(c, clip, store);
        });
    }

    // Shared instrument/effect writer: type id, optional sample, generic parameter map, custom-state blob.
    private static void WriteComponent(OngenWriter w, string typeId, object component,
        IReadOnlyList<Parameter> parameters, SampleStore store, bool enabled, ISampleHost? host)
    {
        w.WriteChunk(c =>
        {
            c.WriteString(typeId);
            c.WriteBool(enabled);

            var sampleRef = host?.CurrentSample is { } buf ? store.Add(buf) : "";
            c.WriteString(sampleRef);
            c.WriteString(host?.SampleName ?? "");

            WriteParameters(c, parameters);

            // Custom state for anything not captured by parameters (e.g. the EQ's band list).
            if (component is IProjectStatefulComponent stateful)
            {
                c.WriteBool(true);
                c.WriteChunk(stateful.WriteProjectState);
            }
            else
            {
                c.WriteBool(false);
            }
        });
    }

    private static void WriteParameters(OngenWriter w, IReadOnlyList<Parameter> parameters)
    {
        w.WriteInt(parameters.Count);
        foreach (var p in parameters)
        {
            w.WriteString(p.Name);
            switch (p)
            {
                case FloatParameter f: w.WriteInt(0); w.WriteDouble(f.Value); break;
                case BoolParameter b: w.WriteInt(1); w.WriteBool(b.Value); break;
                case ChoiceParameter ch: w.WriteInt(2); w.WriteInt(ch.SelectedIndex); break;
                default: w.WriteInt(-1); break;
            }
        }
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
                project.Tracks.Add(ReadTrack(r, instruments, effects, samples, warnings));
        }

        return new LoadResult(project, loopStart, loopEnd, startBeat, warnings, fromNewer);
    }

    private static Track ReadTrack(OngenReader r, IInstrumentRegistry instruments, IEffectRegistry effects,
        SampleLoader samples, List<string> warnings)
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

            // Instrument
            if (c.ReadBool())
                track.Instrument = ReadInstrument(c, instruments, samples, warnings);

            // Effects
            var fxCount = c.ReadInt();
            for (var i = 0; i < fxCount; i++)
            {
                var fx = ReadEffect(c, effects, warnings);
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
        track.CommitEffects();
        track.CommitAutoLanes();
        return track;
    }

    private static IInstrument? ReadInstrument(OngenReader r, IInstrumentRegistry instruments,
        SampleLoader samples, List<string> warnings)
    {
        IInstrument? inst = null;
        r.ReadChunk(c =>
        {
            var typeId = c.ReadString();
            c.ReadBool(); // enabled (unused for instruments)
            var sampleRef = c.ReadString();
            var sampleName = c.ReadString();

            try { inst = instruments.Create(typeId); }
            catch { warnings.Add($"Instrument '{typeId}' is unavailable; the track was loaded without it."); inst = null; }

            var persisted = ReadParameters(c);
            if (inst is not null) ApplyParameters(inst.Parameters, persisted);

            if (inst is ISampleHost host && sampleRef.Length > 0 && samples.Get(sampleRef) is { } buf)
                host.LoadSample(buf, sampleName);

            ReadCustomState(c, inst);
        });
        return inst;
    }

    private static IAudioEffect? ReadEffect(OngenReader r, IEffectRegistry effects, List<string> warnings)
    {
        IAudioEffect? fx = null;
        r.ReadChunk(c =>
        {
            var typeId = c.ReadString();
            var enabled = c.ReadBool();
            c.ReadString(); // sampleRef (effects don't host samples today)
            c.ReadString(); // sampleName

            try { fx = effects.Create(typeId); }
            catch { warnings.Add($"Effect '{typeId}' is unavailable; it was skipped."); fx = null; }

            var persisted = ReadParameters(c);
            if (fx is not null)
            {
                fx.Enabled = enabled;
                ApplyParameters(fx.Parameters, persisted);
            }

            ReadCustomState(c, fx);
        });
        return fx;
    }

    private static void ReadCustomState(OngenReader r, object? component)
    {
        if (!r.ReadBool()) return;
        r.ReadChunk(c => (component as IProjectStatefulComponent)?.ReadProjectState(c));
    }

    private readonly record struct PersistedParam(int Kind, double Number, bool Flag);

    private static List<PersistedParam> ReadParameters(OngenReader r)
    {
        var count = r.ReadInt();
        var list = new List<PersistedParam>(count);
        for (var i = 0; i < count; i++)
        {
            r.ReadString(); // name (matched by index; kept for debugging/future use)
            var kind = r.ReadInt();
            switch (kind)
            {
                case 0: list.Add(new PersistedParam(0, r.ReadDouble(), false)); break;
                case 1: list.Add(new PersistedParam(1, 0, r.ReadBool())); break;
                case 2: list.Add(new PersistedParam(2, r.ReadInt(), false)); break;
                default: list.Add(new PersistedParam(-1, 0, false)); break;
            }
        }

        return list;
    }

    // Applies persisted values to live parameters by index (so duplicate names stay distinct). Mismatched
    // kinds or out-of-range indices are skipped, so added/removed parameters across versions degrade safely.
    private static void ApplyParameters(IReadOnlyList<Parameter> live, List<PersistedParam> persisted)
    {
        var n = Math.Min(live.Count, persisted.Count);
        for (var i = 0; i < n; i++)
        {
            var p = persisted[i];
            switch (live[i])
            {
                case FloatParameter f when p.Kind == 0: f.Value = p.Number; break;
                case BoolParameter b when p.Kind == 1: b.Value = p.Flag; break;
                case ChoiceParameter ch when p.Kind == 2: ch.SelectedIndex = (int)p.Number; break;
            }
        }
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

    private static IAutomationTarget? BuildTarget(Track track, int kind, int effectIndex, int paramIndex)
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
                return track.Instrument is null ? null : FromParameter(track.Instrument.Parameters, paramIndex);
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

    // Collects unique samples for save, keyed by a content hash (incl. channels + rate), so identical buffers
    // are stored once and clips/instruments reference them by hash.
    private sealed class SampleStore
    {
        private readonly Dictionary<string, AudioSampleBuffer> _byHash = new();

        public IEnumerable<KeyValuePair<string, AudioSampleBuffer>> Entries => _byHash;

        public string Add(AudioSampleBuffer buffer)
        {
            var hash = Hash(buffer);
            _byHash.TryAdd(hash, buffer);
            return hash;
        }

        private static string Hash(AudioSampleBuffer b)
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> header = stackalloc byte[8];
            BitConverter.TryWriteBytes(header, b.Channels);
            BitConverter.TryWriteBytes(header[4..], b.SampleRate);
            hasher.AppendData(header);
            hasher.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(b.Samples.AsSpan()));
            return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }
    }

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
