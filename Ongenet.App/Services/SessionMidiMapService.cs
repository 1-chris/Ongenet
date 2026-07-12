using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Services;

/// <summary>
/// Default <see cref="ISessionMidiMapService"/>. Learns and applies session-control mappings. Matching
/// runs on the MIDI thread; triggered playback calls are marshalled to the UI thread.
/// </summary>
public sealed class SessionMidiMapService : ISessionMidiMapService
{
    private readonly IPlaybackModeService _playback;
    private readonly IProjectService _project;
    private readonly IUiThreadDispatcher? _ui;

    private readonly List<SessionMidiMapping> _mappings = new();
    private volatile SessionMidiMapping[] _snapshot = Array.Empty<SessionMidiMapping>();
    private volatile bool _learning;
    private SessionLearnTarget _learnTarget = new() { Action = SessionMidiAction.LaunchSlot };
    private bool _suppressProjectSync;

    public SessionMidiMapService(IPlaybackModeService playback, IProjectService project,
        IUiThreadDispatcher? ui = null)
    {
        _playback = playback;
        _project = project;
        _ui = ui;
        _project.ProjectChanged += LoadFromProject;
        LoadFromProject();
    }

    public IReadOnlyList<SessionMidiMapping> Mappings => _mappings;

    public SessionLearnTarget? LearnTarget => _learning ? _learnTarget : null;

    public event Action? MappingsChanged;
    public event Action? LearnStateChanged;

    public void BeginLearn(SessionMidiAction action, Guid? trackId = null, int? sceneIndex = null)
    {
        _learnTarget = new SessionLearnTarget { Action = action, TrackId = trackId, SceneIndex = sceneIndex };
        _learning = true;
        LearnStateChanged?.Invoke();
    }

    public void CancelLearn()
    {
        if (!_learning) return;
        _learning = false;
        LearnStateChanged?.Invoke();
    }

    public void ClearMapping(SessionMidiAction action, Guid? trackId = null, int? sceneIndex = null)
    {
        _mappings.RemoveAll(m => MappingMatchesContext(m, action, trackId, sceneIndex));
        Publish();
    }

    public bool HandleMessage(MidiMessage message)
    {
        bool isNote;
        bool isRelease;
        int number;
        switch (message.Kind)
        {
            case MidiMessageKind.NoteOn when message.Velocity > 0:
                isNote = true;
                isRelease = false;
                number = message.Note;
                break;
            case MidiMessageKind.NoteOff:
            case MidiMessageKind.NoteOn:
                isNote = true;
                isRelease = true;
                number = message.Note;
                break;
            case MidiMessageKind.ControlChange when message.Value >= 64:
                isNote = false;
                isRelease = false;
                number = message.Controller;
                break;
            default:
                return false;
        }

        if (_learning)
        {
            _learning = false;
            var target = _learnTarget;
            var channel = message.Channel;
            var deviceId = message.SourceDeviceId;
            Post(() =>
            {
                Bind(target, isNote, channel, number, deviceId);
                LearnStateChanged?.Invoke();
            });
            return true;
        }

        foreach (var m in _snapshot)
        {
            if (m.IsNote != isNote || m.Number != number) continue;
            if (m.Channel >= 0 && m.Channel != message.Channel) continue;
            if (!string.IsNullOrEmpty(m.SourceDeviceId)
                && !string.Equals(m.SourceDeviceId, message.SourceDeviceId, StringComparison.Ordinal))
                continue;

            if (isRelease)
            {
                if (m.Action is not (SessionMidiAction.GateOff or SessionMidiAction.GateOn)) continue;
            }
            else if (m.Action == SessionMidiAction.GateOff)
            {
                continue;
            }

            var mapping = m;
            Post(() => Invoke(mapping, isRelease));
            return true;
        }

        return false;
    }

    public void SetMappings(IEnumerable<SessionMidiMapping> mappings)
    {
        _mappings.Clear();
        _mappings.AddRange(mappings.Select(Clone));
        Publish();
    }

    private void LoadFromProject()
    {
        _suppressProjectSync = true;
        try
        {
            SetMappings(_project.Current.SessionMidiMappings);
        }
        finally
        {
            _suppressProjectSync = false;
        }
    }

    private void Bind(SessionLearnTarget target, bool isNote, int channel, int number, string? deviceId)
    {
        _mappings.RemoveAll(m => MappingMatchesContext(m, target.Action, target.TrackId, target.SceneIndex));
        _mappings.Add(new SessionMidiMapping
        {
            Action = target.Action,
            IsNote = isNote,
            Channel = -1,
            Number = number,
            SourceDeviceId = deviceId,
            TrackId = target.TrackId,
            SceneIndex = target.SceneIndex
        });
        Publish();
    }

    private void Invoke(SessionMidiMapping mapping, bool isRelease)
    {
        switch (mapping.Action)
        {
            case SessionMidiAction.LaunchSlot:
                if (TryResolveSessionClip(mapping, out var launchClip))
                    _playback.LaunchClip(launchClip.Id);
                break;
            case SessionMidiAction.LaunchScene when mapping.SceneIndex is { } scene:
                _playback.LaunchScene(scene);
                break;
            case SessionMidiAction.QueueSlot:
                if (TryResolveSessionClip(mapping, out var queueClip))
                    _playback.QueueClip(queueClip.Id);
                break;
            case SessionMidiAction.StopSlot:
                if (TryResolveSessionClip(mapping, out var stopClip))
                    _playback.StopClip(stopClip.Id);
                break;
            case SessionMidiAction.StopScene when mapping.SceneIndex is { } stopScene:
                foreach (var clip in _project.Current.SessionClips.Where(c => c.SceneIndex == stopScene))
                    _playback.StopClip(clip.Id);
                break;
            case SessionMidiAction.StopAll:
                _playback.StopAll();
                break;
            case SessionMidiAction.GateOn:
                if (TryResolveSessionClip(mapping, out var gateClip))
                    _playback.GateClip(gateClip.Id, held: !isRelease);
                break;
            case SessionMidiAction.GateOff:
                if (isRelease && TryResolveSessionClip(mapping, out var releaseClip))
                    _playback.GateClip(releaseClip.Id, held: false);
                break;
        }
    }

    private bool TryResolveSessionClip(SessionMidiMapping mapping, out SessionClip clip)
    {
        clip = null!;
        if (mapping.TrackId is not { } trackId || mapping.SceneIndex is not { } scene) return false;
        clip = _project.Current.SessionClips
            .FirstOrDefault(c => c.TrackId == trackId && c.SceneIndex == scene)!;
        return clip is not null;
    }

    private static bool MappingMatchesContext(SessionMidiMapping m, SessionMidiAction action, Guid? trackId,
        int? sceneIndex)
        => m.Action == action && m.TrackId == trackId && m.SceneIndex == sceneIndex;

    private static SessionMidiMapping Clone(SessionMidiMapping m) => new()
    {
        Action = m.Action,
        IsNote = m.IsNote,
        Channel = m.Channel,
        Number = m.Number,
        SourceDeviceId = m.SourceDeviceId,
        TrackId = m.TrackId,
        SceneIndex = m.SceneIndex
    };

    private void Publish()
    {
        _snapshot = _mappings.Select(Clone).ToArray();
        if (!_suppressProjectSync)
        {
            _project.Current.SessionMidiMappings.Clear();
            _project.Current.SessionMidiMappings.AddRange(_snapshot);
        }
        MappingsChanged?.Invoke();
    }

    private void Post(Action action)
    {
        if (_ui is null) action();
        else _ui.Post(action);
    }
}
