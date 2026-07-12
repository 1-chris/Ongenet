using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Scripting;

/// <summary>Default <see cref="IScriptingApi"/> backed by core project services.</summary>
public sealed partial class ScriptingApi : IScriptingApi, IDisposable
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IHistoryCapture _history;
    private readonly IEventAggregator _events;
    private readonly IInstrumentRegistry _instruments;
    private readonly IEffectRegistry _effects;
    private readonly IProjectScriptExporter? _projectExporter;
    private readonly IPresetScriptExporter? _presetExporter;
    private readonly ScriptingRuntime _runtime;
    private readonly List<string> _output = new();
    private readonly List<IDisposable> _liveSubscriptions = new();
    private readonly List<IDisposable> _clipEventSubscriptions = new();

    public ScriptingApi(
        IProjectService project,
        ITransportService transport,
        IHistoryCapture history,
        IEventAggregator events,
        IInstrumentRegistry instruments,
        IEffectRegistry effects,
        IProjectScriptExporter? projectExporter = null,
        IPresetScriptExporter? presetExporter = null)
    {
        _project = project;
        _transport = transport;
        _history = history;
        _events = events;
        _instruments = instruments;
        _effects = effects;
        _projectExporter = projectExporter;
        _presetExporter = presetExporter;
        _runtime = new ScriptingRuntime(transport);
        _runtime.Configure(msg => Log(msg), uiContext: null);
    }

    public IReadOnlyList<string> OutputLines => _output;
    public event Action? OutputChanged;

    public void Log(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        _output.Add(message);
        OutputChanged?.Invoke();
    }

    public void ClearOutput()
    {
        _output.Clear();
        OutputChanged?.Invoke();
    }

    public string GetProjectName() => _project.Current.Name;

    public double GetTempo() => _project.Current.Tempo.BeatsPerMinute;

    public void SetTempo(double bpm)
    {
        if (bpm <= 0) return;
        if (Math.Abs(_project.Current.Tempo.BeatsPerMinute - bpm) < 1e-9) return;
        _history.Capture("Change tempo");
        var tempo = new Tempo(bpm);
        _transport.Tempo = tempo;
        _project.Current.Tempo = tempo;
    }

    public ScriptTimeSignature GetTimeSignature()
    {
        var ts = _project.Current.TimeSignature;
        return new ScriptTimeSignature(ts.Numerator, ts.Denominator);
    }

    public void SetTimeSignature(int numerator, int denominator)
    {
        if (numerator <= 0 || denominator <= 0) return;
        var next = new TimeSignature(numerator, denominator);
        if (_project.Current.TimeSignature == next) return;
        _history.Capture("Change time signature");
        _project.Current.TimeSignature = next;
    }

    public int GetBarCount() => _project.Current.BarCount;

    public void SetBarCount(int bars)
    {
        if (bars <= 0) return;
        if (_project.Current.BarCount == bars) return;
        _history.Capture("Change arrangement length");
        _project.Current.BarCount = bars;
        _events.Publish(new ArrangementLengthChangedEvent());
    }

    public ScriptTransportState GetTransportState() =>
        _transport.State == TransportState.Playing
            ? ScriptTransportState.Playing
            : ScriptTransportState.Stopped;

    public void Play() => _transport.Play();

    public void Stop() => _transport.Stop();

    public void SeekToBeat(double beat)
    {
        if (beat < 0) beat = 0;
        _transport.StartBeat = beat;
        _transport.NotifyPlayhead(beat);
    }

    public double GetPlayheadBeats() => _transport.PlayheadBeats;

    public ScriptLoopRegion GetLoopRegion() =>
        new(_transport.LoopStart, _transport.LoopEnd, _transport.IsLoopActive);

    public void SetLoopRegion(double start, double end)
    {
        if (end <= start) return;
        _history.Capture("Set loop region");
        _transport.LoopStart = start;
        _transport.LoopEnd = end;
    }

    public void RenameTrack(Guid trackId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var track = FindTrack(trackId);
        if (track is null || track.Name == name) return;
        _history.Capture("Rename track");
        track.Name = name;
        _events.Publish(new TrackChangedEvent(track));
    }

    public IReadOnlyDictionary<Guid, string> GetTrackNames()
        => _project.Current.Tracks.ToDictionary(t => t.Id, t => t.Name);

    public IReadOnlyList<ScriptTrackInfo> GetTracks()
        => _project.Current.Tracks.Select(ToTrackInfo).ToArray();

    public Guid AddInstrumentTrack(string name)
    {
        IInstrument instrument;
        try { instrument = _instruments.Create(InstrumentRegistry.DefaultInstrumentId); }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not create default instrument: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(name))
            name = $"{instrument.Name} {NextInstrumentTrackNumber()}";

        _history.Capture("Add instrument track");
        var track = new Track
        {
            Name = name,
            Kind = TrackKind.Instrument,
            ColorKey = "CatppuccinMauve"
        };
        track.Instruments.Add(new InstrumentSlot(instrument));
        track.CommitInstruments();

        var index = _project.Current.Master is not null ? 1 : 0;
        if (index > _project.Current.Tracks.Count)
            index = _project.Current.Tracks.Count;
        _project.Current.Tracks.Insert(index, track);
        _events.Publish(new TracksChangedEvent());
        return track.Id;
    }

    public void RemoveTrack(Guid trackId)
    {
        var track = FindTrack(trackId);
        if (track is null || track.Kind == TrackKind.Master) return;
        _history.Capture("Delete track");
        _project.Current.Tracks.Remove(track);
        _events.Publish(new TracksChangedEvent());
    }

    public void SetTrackVolume(Guid trackId, double volume)
    {
        var track = FindTrack(trackId);
        if (track is null) return;
        volume = Math.Clamp(volume, 0, 1);
        if (Math.Abs(track.Volume - volume) < 1e-9) return;
        _history.Capture("Change track volume");
        track.Volume = volume;
        _events.Publish(new TrackChangedEvent(track));
    }

    public void SetTrackPan(Guid trackId, double pan)
    {
        var track = FindTrack(trackId);
        if (track is null) return;
        pan = Math.Clamp(pan, -1, 1);
        if (Math.Abs(track.Pan - pan) < 1e-9) return;
        _history.Capture("Change track pan");
        track.Pan = pan;
        _events.Publish(new TrackChangedEvent(track));
    }

    public IReadOnlyList<ScriptClipInfo> GetClips(Guid? trackId = null)
        => ScriptClipOperations.EnumerateClips(_project.Current, trackId)
            .Select(pair => ScriptClipOperations.ToInfo(pair.Track, pair.Clip))
            .ToArray();

    public Guid CreateMidiClip(Guid trackId, string name, double startBeat, double lengthBeats)
    {
        var track = FindTrack(trackId);
        if (track is null) throw new InvalidOperationException($"Track '{trackId}' was not found.");
        if (track.IsBus) throw new InvalidOperationException("Cannot add clips to a bus track.");
        if (lengthBeats <= 0) throw new ArgumentOutOfRangeException(nameof(lengthBeats));

        _history.Capture("Add MIDI clip");
        var clip = new Clip
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Clip" : name,
            StartBeat = Math.Max(0, startBeat),
            LengthBeats = lengthBeats,
            IsAudio = false
        };
        track.Clips.Add(clip);
        _events.Publish(new ClipAddedEvent(track, clip));
        return clip.Id;
    }

    public void DeleteClip(Guid clipId)
    {
        var found = ScriptClipOperations.FindClip(_project.Current, clipId);
        if (found is null) return;
        var (track, clip) = found.Value;
        _history.Capture("Delete clip");
        track.Clips.Remove(clip);
        _events.Publish(new TracksChangedEvent());
    }

    public void MoveClip(Guid clipId, double startBeat)
    {
        var found = ScriptClipOperations.FindClip(_project.Current, clipId);
        if (found is null) return;
        var (_, clip) = found.Value;
        if (startBeat < 0) startBeat = 0;
        if (Math.Abs(clip.StartBeat - startBeat) < 1e-9) return;
        _history.Capture("Move clip");
        clip.StartBeat = startBeat;
        _events.Publish(new ClipChangedEvent(clip));
    }

    public Guid DuplicateClip(Guid clipId, double? offsetBeats = null)
    {
        var found = ScriptClipOperations.FindClip(_project.Current, clipId);
        if (found is null) throw new InvalidOperationException($"Clip '{clipId}' was not found.");
        var (track, clip) = found.Value;
        _history.Capture("Duplicate clip");
        var copy = ScriptClipOperations.DuplicateClip(clip);
        copy.StartBeat = clip.StartBeat + (offsetBeats ?? clip.LengthBeats);
        track.Clips.Add(copy);
        _events.Publish(new ClipAddedEvent(track, copy));
        return copy.Id;
    }

    public void RenameClip(Guid clipId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var found = ScriptClipOperations.FindClip(_project.Current, clipId);
        if (found is null) return;
        var (_, clip) = found.Value;
        if (clip.Name == name) return;
        _history.Capture("Rename clip");
        clip.Name = name;
        _events.Publish(new ClipChangedEvent(clip));
    }

    public void QuantizeAllMidiClips(double gridBeats) => ApplyToAllMidiClips("Quantize all MIDI clips", c => LogicalMidiEdit.QuantizeClip(c, gridBeats));

    public void QuantizeClip(Guid clipId, double gridBeats)
    {
        if (gridBeats <= 0) return;
        var found = ScriptClipOperations.FindClip(_project.Current, clipId);
        if (found is null) return;
        var (_, clip) = found.Value;
        if (clip.IsAudio) return;
        _history.Capture("Quantize clip");
        LogicalMidiEdit.QuantizeClip(clip, gridBeats);
        _events.Publish(new ClipNotesChangedEvent(clip));
    }

    public void TransposeAllMidiClips(int semitones) =>
        ApplyToAllMidiClips("Transpose all MIDI clips", c => LogicalMidiEdit.TransposeClip(c, semitones));

    public void ScaleAllMidiVelocities(double factor) =>
        ApplyToAllMidiClips("Scale all MIDI velocities", c => LogicalMidiEdit.ScaleVelocity(c, factor));

    public void HumanizeAllMidiClips(int maxTicks) =>
        ApplyToAllMidiClips("Humanize all MIDI clips", c => LogicalMidiEdit.HumanizeClip(c, maxTicks));

    public void ApplyChanceToAllMidiClips(float chance) =>
        ApplyToAllMidiClips("Apply chance to all MIDI clips", c => LogicalMidiEdit.ApplyChance(c, chance));

    public IDisposable OnTransportStateChanged(Action<ScriptTransportState> handler)
    {
        EnsureLiveActive();
        var sub = _runtime.OnTransportStateChanged(handler);
        _liveSubscriptions.Add(sub);
        return sub;
    }

    public IDisposable OnBeat(Action<double> handler, double gridBeats = 1.0)
    {
        EnsureLiveActive();
        var sub = _runtime.OnBeat(handler, gridBeats);
        _liveSubscriptions.Add(sub);
        return sub;
    }

    public IDisposable OnClipChanged(Action<ScriptClipInfo> handler)
    {
        EnsureLiveActive();
        var sub = _runtime.OnClipChanged(handler);
        _liveSubscriptions.Add(sub);
        return sub;
    }

    public void StopLive()
    {
        foreach (var sub in _liveSubscriptions)
            sub.Dispose();
        _liveSubscriptions.Clear();
        foreach (var sub in _clipEventSubscriptions)
            sub.Dispose();
        _clipEventSubscriptions.Clear();
        _runtime.Deactivate();
    }

    public void BeginLiveSession(SynchronizationContext? uiContext)
    {
        StopLive();
        _runtime.Configure(msg => Log(msg), uiContext);
        _runtime.Activate();
        _clipEventSubscriptions.Add(_events.Subscribe<ClipChangedEvent>(e => NotifyClip(e.Clip)));
        _clipEventSubscriptions.Add(_events.Subscribe<ClipNotesChangedEvent>(e => NotifyClip(e.Clip)));
    }

    public void Dispose() => StopLive();

    private void EnsureLiveActive()
    {
        if (!_runtime.IsActive)
            throw new InvalidOperationException("Live scripting is not active. Start a live script first.");
    }

    private void NotifyClip(Clip clip)
    {
        var found = ScriptClipOperations.FindClip(_project.Current, clip.Id);
        if (found is null) return;
        var (track, c) = found.Value;
        _runtime.NotifyClipChanged(ScriptClipOperations.ToInfo(track, c));
    }

    private void ApplyToAllMidiClips(string label, Action<Clip> edit)
    {
        _history.Capture(label);
        foreach (var track in _project.Current.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.IsAudio) continue;
                edit(clip);
                _events.Publish(new ClipNotesChangedEvent(clip));
            }
        }
    }

    private Track? FindTrack(Guid trackId) => ScriptingApiSupport.FindTrack(_project.Current, trackId);

    private int NextInstrumentTrackNumber() =>
        _project.Current.Tracks.Count(t => t.Kind == TrackKind.Instrument) + 1;

    private static ScriptTrackInfo ToTrackInfo(Track track) => ScriptingApiSupport.ToTrackInfo(track);
}
