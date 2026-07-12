using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>Default <see cref="ISessionCaptureService"/>.</summary>
public sealed class SessionCaptureService : ISessionCaptureService
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IEventAggregator _events;
    private readonly List<CapturedLaunch> _log = new();

    public SessionCaptureService(IProjectService project, ITransportService transport, IEventAggregator events)
    {
        _project = project;
        _transport = transport;
        _events = events;
        _transport.StateChanged += OnTransportStateChanged;
        _project.ProjectChanged += () => { if (_log.Count > 0) ClearLog(); };
    }

    public bool SessionRecordArmed { get; private set; }

    public bool CommitOnTransportStop { get; set; }

    public int PendingLaunchCount => _log.Count;

    public event Action? SessionRecordArmedChanged;
    public event Action? PendingChanged;

    public void SetSessionRecordArmed(bool armed)
    {
        if (SessionRecordArmed == armed) return;
        SessionRecordArmed = armed;
        if (!armed) ClearLog();
        SessionRecordArmedChanged?.Invoke();
    }

    public void LogLaunch(Guid sessionClipId, double launchBeat)
    {
        if (!SessionRecordArmed || _transport.State != TransportState.Playing) return;

        var sc = _project.Current.SessionClips.FirstOrDefault(c => c.Id == sessionClipId);
        if (sc?.SourceClipId is null) return;

        _log.Add(new CapturedLaunch(sessionClipId, sc.TrackId, sc.SourceClipId.Value, launchBeat, sc.LengthBeats));
        PendingChanged?.Invoke();
    }

    public void Capture() => Materialize();

    public void OnTransportStopped()
    {
        if (CommitOnTransportStop && SessionRecordArmed && _log.Count > 0)
            Materialize();
    }

    private void OnTransportStateChanged(TransportState state)
    {
        if (state == TransportState.Stopped)
            OnTransportStopped();
    }

    private void ClearLog()
    {
        if (_log.Count == 0) return;
        _log.Clear();
        PendingChanged?.Invoke();
    }

    private void Materialize()
    {
        if (_log.Count == 0) return;

        foreach (var launch in _log)
        {
            var track = _project.Current.Tracks.FirstOrDefault(t => t.Id == launch.TrackId);
            if (track is null) continue;

            var source = track.Clips.FirstOrDefault(c => c.Id == launch.SourceClipId);
            if (source is null) continue;

            var clip = CloneToArrangement(source, launch.LaunchBeat, launch.LengthBeats, launch.SessionClipId);
            track.Clips.Add(clip);
            _events.Publish(new ClipAddedEvent(track, clip));
        }

        _log.Clear();
        PendingChanged?.Invoke();
    }

    private static Clip CloneToArrangement(Clip source, double startBeat, double lengthBeats, Guid sessionClipId)
    {
        var clip = new Clip
        {
            Name = source.Name,
            StartBeat = startBeat,
            LengthBeats = lengthBeats,
            IsAudio = source.IsAudio,
            StretchToTempo = source.StretchToTempo,
            SourceTempo = source.SourceTempo,
            SourceKey = source.SourceKey,
            PitchCorrected = source.PitchCorrected,
            SourceOffsetSeconds = source.SourceOffsetSeconds,
            SourceLengthSeconds = source.SourceLengthSeconds,
            AudioFilePath = source.AudioFilePath,
            Samples = source.Samples,
            Waveform = source.Waveform,
            WarpMode = source.WarpMode,
            UserFadeInBeats = source.UserFadeInBeats,
            UserFadeOutBeats = source.UserFadeOutBeats,
            Origin = ClipOrigin.CapturedSession,
            CapturedFromSessionClipId = sessionClipId
        };

        foreach (var wm in source.WarpMarkers)
            clip.WarpMarkers.Add(new WarpMarker { SourceSeconds = wm.SourceSeconds, BeatPosition = wm.BeatPosition });

        foreach (var note in source.Notes)
        {
            if (note.StartBeat >= lengthBeats) continue;
            var relLen = note.LengthBeats;
            if (note.StartBeat + relLen > lengthBeats) relLen = lengthBeats - note.StartBeat;
            clip.Notes.Add(new MidiNote
            {
                Note = note.Note,
                StartBeat = note.StartBeat,
                LengthBeats = relLen,
                Velocity = note.Velocity
            });
        }

        return clip;
    }

    private sealed record CapturedLaunch(Guid SessionClipId, Guid TrackId, Guid SourceClipId, double LaunchBeat,
        double LengthBeats);
}
