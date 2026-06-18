using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Default <see cref="IRecordingService"/>. Captures the live preview-note stream against the
/// transport playhead into a fresh "Recorded" MIDI clip per armed track. The take clip and its
/// notes are built and grown <b>live</b> (via <see cref="RefreshLive"/>, pumped by the timeline's
/// frame timer) so the user sees the recording fill in as they play.
/// </summary>
public sealed class RecordingService : IRecordingService
{
    /// <summary>Shortest note that can be captured, in beats (avoids zero-length notes).</summary>
    private const double MinNoteBeats = 0.0625;

    private readonly ITransportService _transport;
    private readonly IProjectService _project;
    private readonly ISelectionService _selection;
    private readonly IPreviewService _preview;
    private readonly IEventAggregator _events;

    private readonly List<LiveTake> _takes = new();
    private readonly Dictionary<int, OpenNote> _open = new(); // note -> note being captured
    private readonly List<AutoCapture> _autoCaptures = new();
    private bool _autoStarted;

    private bool _isRecording;
    private volatile bool _capturing;
    private double _recordStartBeat;

    public RecordingService(ITransportService transport, IProjectService project,
        ISelectionService selection, IPreviewService preview, IEventAggregator events)
    {
        _transport = transport;
        _project = project;
        _selection = selection;
        _preview = preview;
        _events = events;

        _preview.NotePressed += OnNotePressed;
        _preview.NoteReleased += OnNoteReleased;
        _transport.CountInFinished += OnCountInFinished;
    }

    public bool IsRecording => _isRecording;

    public event Action? StateChanged;

    public void StartRecording()
    {
        if (_isRecording) return;

        var targets = _project.Current.Tracks
            .Where(t => t is { IsArmed: true, Kind: TrackKind.Instrument })
            .ToList();
        if (targets.Count == 0 && _selection.SelectedTrack is { Kind: TrackKind.Instrument } selected)
        {
            targets.Add(selected);
        }

        // Armed automation lanes (across all tracks) capture the control's movements.
        var armedLanes = _project.Current.Tracks
            .SelectMany(t => t.AutoLanes.Where(l => l.IsArmed).Select(l => new AutoCapture(t, l)))
            .ToList();

        if (targets.Count == 0 && armedLanes.Count == 0) return; // nothing to record into

        _takes.Clear();
        _takes.AddRange(targets.Select(t => new LiveTake(t)));
        _autoCaptures.Clear();
        _autoCaptures.AddRange(armedLanes);
        _autoStarted = false;
        _open.Clear();
        _capturing = false;
        _recordStartBeat = _transport.StartBeat;

        // Route live monitoring to the first instrument target so the user hears what they're recording.
        if (targets.Count > 0) _selection.SelectTrack(targets[0]);

        _transport.CountInBeats = BeatsPerBar;
        _transport.IsRecording = true;
        _isRecording = true;
        StateChanged?.Invoke();

        _transport.Play();
    }

    public void StopRecording()
    {
        if (!_isRecording) return;

        // Close any still-held notes at the final playhead and flush one last live update.
        var endBeat = _transport.PlayheadBeats;
        foreach (var open in _open.Values) open.EndBeat ??= endBeat;
        RefreshLive();

        // Tidy each take clip's length up to whole bars so it sits cleanly on the grid.
        var beatsPerBar = BeatsPerBar;
        foreach (var take in _takes.Where(t => t.Clip is not null))
        {
            var clip = take.Clip!;
            var bars = Math.Max(1, (int)Math.Ceiling(clip.LengthBeats / beatsPerBar));
            clip.LengthBeats = bars * beatsPerBar;
            _events.Publish(new ClipChangedEvent(clip));
        }

        // Anchor each recorded automation lane at the final value and re-sort.
        foreach (var cap in _autoCaptures)
        {
            if (_autoStarted) cap.Lane.AddPoint(new AutomationPoint(endBeat, cap.Lane.Target.Read()));
            cap.Lane.Sort();
            cap.Track.CommitAutoLanes();
            _events.Publish(new AutomationChangedEvent(cap.Track));
        }

        _capturing = false;
        _isRecording = false;
        _transport.IsRecording = false;
        _open.Clear();
        _takes.Clear();
        _autoCaptures.Clear();

        _transport.Stop();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Pumps the live take(s): lazily creates the clip on the first note, places/grows captured
    /// notes, and extends the clip to the playhead. Called on the UI thread by the timeline timer.
    /// </summary>
    public void RefreshLive()
    {
        if (!_capturing) return;

        SampleAutomation();

        if (_open.Count == 0 && !TakesStarted) return; // no MIDI captured yet — no clip

        EnsureTakeClips();
        var playhead = _transport.PlayheadBeats;

        // Place/grow each captured note; finalised ones (with an end) are committed and dropped.
        foreach (var note in _open.Keys.ToList())
        {
            var open = _open[note];
            open.Models ??= CreateNoteModels(note, open.StartBeat);

            var end = open.EndBeat ?? playhead;
            var length = Math.Max(MinNoteBeats, end - open.StartBeat);
            foreach (var model in open.Models) model.LengthBeats = length;

            if (open.EndBeat.HasValue) _open.Remove(note);
        }

        // Grow each take clip to cover the playhead, then repaint.
        foreach (var take in _takes)
        {
            var clip = take.Clip!;
            var needed = playhead - clip.StartBeat;
            if (needed > clip.LengthBeats) clip.LengthBeats = needed;
            _events.Publish(new ClipChangedEvent(clip));
            _events.Publish(new ClipNotesChangedEvent(clip));
        }
    }

    // Samples each armed automation lane's control value into points (UI thread). On the first pass it
    // clears the punched range and seeds the start value; thereafter it adds a point when the value moves.
    private void SampleAutomation()
    {
        if (_autoCaptures.Count == 0) return;
        var playhead = _transport.PlayheadBeats;

        if (!_autoStarted)
        {
            foreach (var cap in _autoCaptures)
            {
                cap.Lane.Points.RemoveAll(p => p.Beat >= _recordStartBeat - 1e-6);
                cap.Last = cap.Lane.Target.Read();
                cap.Lane.AddPoint(new AutomationPoint(_recordStartBeat, cap.Last));
                _events.Publish(new AutomationChangedEvent(cap.Track));
            }

            _autoStarted = true;
        }

        foreach (var cap in _autoCaptures)
        {
            if (playhead <= _recordStartBeat) continue;
            var v = cap.Lane.Target.Read();
            var range = Math.Max(1e-9, cap.Lane.Maximum - cap.Lane.Minimum);
            if (Math.Abs(v - cap.Last) <= range * 0.005) continue;
            cap.Lane.AddPoint(new AutomationPoint(playhead, v));
            cap.Last = v;
            _events.Publish(new AutomationChangedEvent(cap.Track));
        }
    }

    // Count-in has elapsed (audio thread): begin capturing input against the moving playhead.
    private void OnCountInFinished()
    {
        if (_isRecording) _capturing = true;
    }

    private void OnNotePressed(int note)
    {
        if (!_capturing) return;
        _open[note] = new OpenNote { StartBeat = _transport.PlayheadBeats };
    }

    private void OnNoteReleased(int note)
    {
        if (!_capturing) return;
        if (_open.TryGetValue(note, out var open)) open.EndBeat = _transport.PlayheadBeats;
    }

    private bool TakesStarted => _takes.Count > 0 && _takes[0].Clip is not null;

    // Creates the take clips on the first captured note and announces them to the timeline.
    private void EnsureTakeClips()
    {
        if (TakesStarted) return;
        foreach (var take in _takes)
        {
            var clip = new Clip { Name = "Recorded", StartBeat = _recordStartBeat, LengthBeats = MinNoteBeats };
            take.Clip = clip;
            take.Track.Clips.Add(clip);
            _events.Publish(new ClipAddedEvent(take.Track, clip));
        }
    }

    // Adds a note model (clip-relative) to every take clip and returns the created models.
    private List<MidiNote> CreateNoteModels(int note, double startBeatAbs)
    {
        var models = new List<MidiNote>(_takes.Count);
        foreach (var take in _takes)
        {
            var clip = take.Clip!;
            var model = new MidiNote
            {
                Note = note,
                StartBeat = Math.Max(0, startBeatAbs - clip.StartBeat),
                LengthBeats = MinNoteBeats,
                Velocity = 0.9f
            };
            clip.Notes.Add(model);
            models.Add(model);
        }

        return models;
    }

    private int BeatsPerBar => Math.Max(1, _project.Current.TimeSignature.Numerator);

    private sealed class LiveTake
    {
        public LiveTake(Track track) => Track = track;
        public Track Track { get; }
        public Clip? Clip { get; set; }
    }

    private sealed class OpenNote
    {
        public double StartBeat { get; init; }
        public double? EndBeat { get; set; }
        public List<MidiNote>? Models { get; set; }
    }

    // A lane being recorded into, plus the owning track and the last sampled value (for thinning).
    private sealed class AutoCapture
    {
        public AutoCapture(Track track, AutomationLane lane)
        {
            Track = track;
            Lane = lane;
        }

        public Track Track { get; }
        public AutomationLane Lane { get; }
        public double Last { get; set; }
    }
}
