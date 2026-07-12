using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>Default <see cref="IPlaybackModeService"/>.</summary>
public sealed class PlaybackModeService : IPlaybackModeService
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly Dictionary<Guid, SessionClipLaunchState> _launches = new();
    private readonly HashSet<Guid> _queued = new();
    private readonly HashSet<Guid> _gated = new();
    private readonly Dictionary<Guid, double> _queuedAtBeat = new();
    private PlaybackMode _mode = PlaybackMode.Arrangement;
    private double _sessionCrossfader;
    private double _lastProcessedBeat = double.NegativeInfinity;
    private bool _syncingProject;

    public PlaybackModeService(IProjectService project, ITransportService transport)
    {
        _project = project;
        _transport = transport;
        _project.ProjectChanged += OnProjectChanged;
        _transport.StateChanged += OnTransportStateChanged;
        SyncFromProject();
    }

    public PlaybackMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            if (!_syncingProject) _project.Current.PlaybackMode = value;
            ModeChanged?.Invoke();
        }
    }

    public double LaunchQuantizeBeats
    {
        get => _project.Current.LaunchQuantizeBeats;
        set
        {
            if (Math.Abs(_project.Current.LaunchQuantizeBeats - value) < 1e-9) return;
            _project.Current.LaunchQuantizeBeats = value;
        }
    }

    public double SessionCrossfader
    {
        get => _sessionCrossfader;
        set => _sessionCrossfader = Math.Clamp(value, 0, 1);
    }

    public IReadOnlyCollection<Guid> ActiveSessionClipIds => _launches.Keys;

    public IReadOnlyDictionary<Guid, SessionClipLaunchState> ActiveLaunches => _launches;

    public event Action? ModeChanged;
    public event Action? ActiveClipsChanged;

    public void LaunchClip(Guid sessionClipId) => LaunchClipInternal(sessionClipId, forceImmediate: false);

    public void QueueClip(Guid sessionClipId)
    {
        var clip = FindSessionClip(sessionClipId);
        if (clip is null) return;

        _queued.Add(sessionClipId);
        clip.IsQueued = true;
        clip.IsPlaying = false;

        var quantize = EffectiveQuantizeBeats(clip);
        _queuedAtBeat[sessionClipId] = ResolveLaunchBeat(quantize);

        if (_transport.State != TransportState.Playing)
            return;

        if (quantize <= 0)
            LaunchClipInternal(sessionClipId, forceImmediate: true);
        else
            NotifyActiveChanged();
    }

    public void GateClip(Guid sessionClipId, bool held)
    {
        var clip = FindSessionClip(sessionClipId);
        if (clip is null || clip.LaunchMode != SessionLaunchMode.Gate) return;

        if (held)
        {
            _gated.Add(sessionClipId);
            LaunchClipInternal(sessionClipId, forceImmediate: true);
        }
        else
        {
            _gated.Remove(sessionClipId);
            StopClip(sessionClipId);
        }
    }

    public void StopClip(Guid sessionClipId)
    {
        _queued.Remove(sessionClipId);
        _queuedAtBeat.Remove(sessionClipId);
        _gated.Remove(sessionClipId);
        if (!_launches.Remove(sessionClipId)) return;
        var clip = FindSessionClip(sessionClipId);
        if (clip is not null)
        {
            clip.IsPlaying = false;
            clip.IsQueued = false;
        }

        NotifyActiveChanged();
    }

    public void StopTrack(Guid trackId)
    {
        var removed = false;
        foreach (var id in _launches.Keys.ToArray())
        {
            if (_launches[id].Clip.TrackId != trackId) continue;
            StopClip(id);
            removed = true;
        }

        foreach (var id in _queued.ToArray())
        {
            var clip = FindSessionClip(id);
            if (clip?.TrackId != trackId) continue;
            _queued.Remove(id);
            _queuedAtBeat.Remove(id);
            clip.IsQueued = false;
            removed = true;
        }

        if (removed) NotifyActiveChanged();
    }

    public void LaunchScene(int sceneIndex)
    {
        var byTrack = new Dictionary<Guid, SessionClip>();
        foreach (var sc in _project.Current.SessionClips)
        {
            if (sc.SceneIndex != sceneIndex) continue;
            byTrack[sc.TrackId] = sc;
        }

        if (byTrack.Count == 0) return;

        foreach (var sc in byTrack.Values)
            LaunchClip(sc.Id);
    }

    public void StopAll()
    {
        if (_launches.Count == 0 && _queued.Count == 0) return;
        foreach (var id in _launches.Keys.ToArray())
        {
            var clip = FindSessionClip(id);
            if (clip is not null)
            {
                clip.IsPlaying = false;
                clip.IsQueued = false;
            }
        }

        foreach (var id in _queued.ToArray())
        {
            var clip = FindSessionClip(id);
            if (clip is not null) clip.IsQueued = false;
        }

        _launches.Clear();
        _queued.Clear();
        _queuedAtBeat.Clear();
        _gated.Clear();
        NotifyActiveChanged();
    }

    /// <summary>Called from the audio engine each block to fire queued launches on quantize boundaries.</summary>
    public void ProcessPlayhead(double beat)
    {
        if (_queued.Count == 0) return;

        foreach (var id in _queued.ToArray())
        {
            if (!_queuedAtBeat.TryGetValue(id, out var target)) continue;
            if (beat + 1e-6 < target) continue;
            _queued.Remove(id);
            _queuedAtBeat.Remove(id);
            LaunchClipInternal(id, forceImmediate: true);
        }

        _lastProcessedBeat = beat;
    }

    public void TickFollowActions(double prevBeat, double curBeat)
    {
        if (curBeat <= prevBeat) return;

        foreach (var launch in _launches.Values.ToArray())
        {
            var clip = launch.Clip;
            if (launch.Looping) continue;
            if (clip.LaunchMode == SessionLaunchMode.Gate && _gated.Contains(clip.Id)) continue;

            var end = launch.LaunchBeat + clip.LengthBeats;
            if (prevBeat >= end - 1e-9 || curBeat < end - 1e-9) continue;

            var target = SessionScheduler.ResolveFollowTarget(clip, _project.Current.SessionClips, Random.Shared);
            StopClip(clip.Id);
            if (target is not null) LaunchClip(target.Id);
        }
    }

    private void OnProjectChanged()
    {
        _launches.Clear();
        _queued.Clear();
        _queuedAtBeat.Clear();
        _gated.Clear();
        SyncFromProject();
        NotifyActiveChanged();
    }

    private void SyncFromProject()
    {
        _syncingProject = true;
        _mode = _project.Current.PlaybackMode;
        _syncingProject = false;
        ModeChanged?.Invoke();
    }

    private void OnTransportStateChanged(TransportState state)
    {
        if (state != TransportState.Playing) return;
        foreach (var id in _queued.ToArray())
        {
            var clip = FindSessionClip(id);
            if (clip is null) continue;
            _queuedAtBeat[id] = ResolveLaunchBeat(EffectiveQuantizeBeats(clip));
        }

        if (LaunchQuantizeBeats <= 0)
        {
            foreach (var id in _queued.ToArray())
            {
                _queued.Remove(id);
                _queuedAtBeat.Remove(id);
                LaunchClipInternal(id, forceImmediate: true);
            }
        }
    }

    private void LaunchClipInternal(Guid sessionClipId, bool forceImmediate)
    {
        var clip = FindSessionClip(sessionClipId);
        if (clip is null) return;

        if (clip.LaunchMode == SessionLaunchMode.Toggle && _launches.ContainsKey(sessionClipId))
        {
            StopClip(sessionClipId);
            return;
        }

        _queued.Remove(sessionClipId);
        _queuedAtBeat.Remove(sessionClipId);

        StopExclusiveOnTrack(clip.TrackId, except: sessionClipId);

        var perClipQuantize = clip.LaunchQuantizeBeats > 0 ? clip.LaunchQuantizeBeats : LaunchQuantizeBeats;
        var launchBeat = forceImmediate || perClipQuantize <= 0
            ? ResolveLaunchBeat(0)
            : ResolveLaunchBeat(perClipQuantize);

        _launches[sessionClipId] = new SessionClipLaunchState { Clip = clip, LaunchBeat = launchBeat };
        clip.IsPlaying = true;
        clip.IsQueued = false;
        NotifyActiveChanged();
    }

    private double EffectiveQuantizeBeats(SessionClip clip)
        => clip.LaunchQuantizeBeats > 0 ? clip.LaunchQuantizeBeats : LaunchQuantizeBeats;

    private SessionClip? FindSessionClip(Guid id)
        => _project.Current.SessionClips.FirstOrDefault(c => c.Id == id);

    private double ResolveLaunchBeat(double quantizeBeats)
    {
        if (_transport.State != TransportState.Playing)
            return _transport.StartBeat;

        var beat = _transport.PlayheadBeats;
        return quantizeBeats > 0 ? MidiQuantize.SnapForward(beat, quantizeBeats) : beat;
    }

    private void StopExclusiveOnTrack(Guid trackId, Guid except)
    {
        foreach (var id in _launches.Keys.ToArray())
        {
            if (id == except) continue;
            if (_launches[id].Clip.TrackId != trackId) continue;
            StopClip(id);
        }
    }

    private void NotifyActiveChanged() => ActiveClipsChanged?.Invoke();
}
