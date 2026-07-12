using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Scripting;

/// <summary>Default <see cref="IScriptingApi"/> backed by core project services.</summary>
public sealed class ScriptingApi : IScriptingApi
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IHistoryCapture _history;
    private readonly IEventAggregator _events;

    public ScriptingApi(
        IProjectService project,
        ITransportService transport,
        IHistoryCapture history,
        IEventAggregator events)
    {
        _project = project;
        _transport = transport;
        _history = history;
        _events = events;
    }

    public string GetProjectName() => _project.Current.Name;

    public void SetTempo(double bpm)
    {
        if (bpm <= 0) return;
        if (Math.Abs(_project.Current.Tempo.BeatsPerMinute - bpm) < 1e-9) return;
        _history.Capture("Change tempo");
        var tempo = new Tempo(bpm);
        _transport.Tempo = tempo;
        _project.Current.Tempo = tempo;
    }

    public void RenameTrack(Guid trackId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var track = _project.Current.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null || track.Name == name) return;
        _history.Capture("Rename track");
        track.Name = name;
        _events.Publish(new TrackChangedEvent(track));
    }

    public IReadOnlyDictionary<Guid, string> GetTrackNames()
        => _project.Current.Tracks.ToDictionary(t => t.Id, t => t.Name);

    public void QuantizeAllMidiClips(double gridBeats)
    {
        if (gridBeats <= 0) return;
        _history.Capture("Quantize all MIDI clips");
        foreach (var track in _project.Current.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.IsAudio) continue;
                LogicalMidiEdit.QuantizeClip(clip, gridBeats);
                _events.Publish(new ClipNotesChangedEvent(clip));
            }
        }
    }
}
