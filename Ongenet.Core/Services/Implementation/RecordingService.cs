using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>
/// Default <see cref="IRecordingService"/>. Captures, against the transport playhead, into a fresh
/// "Recorded" clip per armed track: the live preview-note stream into armed <b>instrument</b> tracks,
/// armed automation lanes, and the audio-input stream into armed <b>audio</b> tracks. Take clips and
/// their contents are built and grown <b>live</b> (via <see cref="RefreshLive"/>, pumped by the
/// timeline's frame timer) so the user sees the recording fill in as it happens.
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
    private readonly IAudioInput _audioInput;

    private readonly List<LiveTake> _takes = new();
    private readonly Dictionary<int, OpenNote> _open = new(); // note -> note being captured
    private readonly List<AutoCapture> _autoCaptures = new();
    private bool _autoStarted;

    // Audio capture: armed audio tracks record the input stream. The audio thread copies each block
    // into _captureQueue; RefreshLive (UI thread) drains it into the growing take buffers + waveforms.
    private readonly List<AudioTake> _audioTakes = new();
    private readonly ConcurrentQueue<float[]> _captureQueue = new();
    private int _captureChannels = 1;
    private int _captureRate = 44100;

    private bool _isRecording;
    private volatile bool _capturing;
    private double _recordStartBeat;

    public RecordingService(ITransportService transport, IProjectService project,
        ISelectionService selection, IPreviewService preview, IEventAggregator events, IAudioInput audioInput)
    {
        _transport = transport;
        _project = project;
        _selection = selection;
        _preview = preview;
        _events = events;
        _audioInput = audioInput;

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

        // Armed audio tracks capture the audio input stream.
        var audioTargets = _project.Current.Tracks
            .Where(t => t is { IsArmed: true, Kind: TrackKind.Audio })
            .ToList();

        if (targets.Count == 0 && armedLanes.Count == 0 && audioTargets.Count == 0) return; // nothing to record into

        _takes.Clear();
        _takes.AddRange(targets.Select(t => new LiveTake(t)));
        _autoCaptures.Clear();
        _autoCaptures.AddRange(armedLanes);
        _audioTakes.Clear();
        _audioTakes.AddRange(audioTargets.Select(t => new AudioTake(t)));
        _captureQueue.Clear();
        _autoStarted = false;
        _open.Clear();
        _capturing = false;
        _recordStartBeat = _transport.StartBeat;

        // Route live monitoring to the first instrument target so the user hears what they're recording.
        if (targets.Count > 0) _selection.SelectTrack(targets[0]);

        // Open the input stream now (so the device is hot through the count-in); blocks are only
        // queued once capturing actually begins (after the count-in).
        if (_audioTakes.Count > 0)
        {
            try
            {
                _audioInput.Start(OnCapture);
                _captureChannels = Math.Max(1, _audioInput.Format.Channels);
                _captureRate = _audioInput.Format.SampleRate;
            }
            catch
            {
                // No usable input device — drop the audio takes and record whatever else is armed.
                _audioTakes.Clear();
            }
        }

        _transport.CountInBeats = BeatsPerBar;
        _transport.IsRecording = true;
        _isRecording = true;
        StateChanged?.Invoke();

        _transport.Play();
    }

    public void StopRecording()
    {
        if (!_isRecording) return;

        // Stop the input device so no more blocks queue; remaining queued blocks are drained below.
        if (_audioInput.IsCapturing) _audioInput.Stop();

        // Close any still-held notes at the final playhead and flush one last live update (this also
        // drains the final captured audio blocks while _capturing is still true).
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

        // Finalise each audio take: materialise the captured PCM into the clip (kept in memory for
        // now — disk persistence comes with project save/load), flush the waveform, tidy the length.
        foreach (var take in _audioTakes.Where(t => t.Clip is not null))
        {
            var clip = take.Clip!;
            clip.Samples = new AudioSampleBuffer(take.Samples.ToArray(), _captureChannels, _captureRate);
            take.Waveform?.Flush();
            var bars = Math.Max(1, (int)Math.Ceiling(clip.LengthBeats / beatsPerBar));
            clip.LengthBeats = bars * beatsPerBar;
            _events.Publish(new ClipChangedEvent(clip));
        }

        _capturing = false;
        _isRecording = false;
        _transport.IsRecording = false;
        _open.Clear();
        _takes.Clear();
        _autoCaptures.Clear();
        _audioTakes.Clear();
        _captureQueue.Clear();

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
        PumpAudioTakes();

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

    // Audio-input callback (audio thread). Copies each captured block onto the queue once capturing
    // has begun; the UI thread drains it in RefreshLive. Must not block or allocate beyond the copy.
    private void OnCapture(ReadOnlySpan<float> input, int channels)
    {
        if (!_capturing) return;
        _captureQueue.Enqueue(input.ToArray());
    }

    // Drains queued input blocks into every audio take's growing PCM buffer + live waveform.
    private void DrainCapture()
    {
        if (_audioTakes.Count == 0) { _captureQueue.Clear(); return; }
        while (_captureQueue.TryDequeue(out var block))
        {
            foreach (var take in _audioTakes)
            {
                take.Samples.AddRange(block);
                take.Waveform?.Append(block, _captureChannels);
            }
        }
    }

    // Creates audio take clips (on first capture), drains input into them, and grows them to the playhead.
    private void PumpAudioTakes()
    {
        if (_audioTakes.Count == 0) return;

        EnsureAudioTakeClips();
        DrainCapture();

        var playhead = _transport.PlayheadBeats;
        foreach (var take in _audioTakes)
        {
            var clip = take.Clip!;
            var needed = playhead - clip.StartBeat;
            if (needed > clip.LengthBeats) clip.LengthBeats = needed;
            _events.Publish(new ClipChangedEvent(clip));
        }
    }

    // Creates the audio take clips once, anchored at the record start, with a growable live waveform.
    private void EnsureAudioTakeClips()
    {
        if (_audioTakes.Count == 0 || _audioTakes[0].Clip is not null) return;
        foreach (var take in _audioTakes)
        {
            var waveform = new AudioWaveform(samplesPerBucket: 128, sampleRate: _captureRate);
            var clip = new Clip
            {
                Name = "Recorded",
                StartBeat = _recordStartBeat,
                LengthBeats = MinNoteBeats,
                IsAudio = true,
                Waveform = waveform
            };
            take.Clip = clip;
            take.Waveform = waveform;
            take.Track.Clips.Add(clip);
            _events.Publish(new ClipAddedEvent(take.Track, clip));
        }
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

    // An audio track being recorded into: the growing interleaved PCM and its live waveform.
    private sealed class AudioTake
    {
        public AudioTake(Track track) => Track = track;
        public Track Track { get; }
        public Clip? Clip { get; set; }
        public List<float> Samples { get; } = new();
        public AudioWaveform? Waveform { get; set; }
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
