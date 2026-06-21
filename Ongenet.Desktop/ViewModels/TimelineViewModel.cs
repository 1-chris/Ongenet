using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.ViewModels.Timeline;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// The centre arrange view: a ruler plus track lanes built from the current project.
    /// Owns the shared <see cref="TimelineMetrics"/>, reports selection to the
    /// <see cref="ISelectionService"/>, and implements track-level actions for the lanes.
    /// </summary>
    public class TimelineViewModel : ViewModelBase, ITrackActions, IClipActions
    {
        private readonly IProjectService _project;
        private readonly ISelectionService _selection;
        private readonly IAudioFileService _audioFiles;
        private readonly IEventAggregator _events;
        private readonly ITransportService _transport;
        private readonly IInstrumentRegistry _instruments;
        private readonly IEditModeService _editMode;
        private readonly IRecordingService _recording;
        private readonly Services.IHistoryService _history;

        // Canonical per-track lanes (one per track). The rendered <see cref="Lanes"/> collection
        // interleaves these with their (expanded) automation rows; both reference the same track lane
        // instances, so clip edits on a track row show up without rebuilding the rendered rows.
        private readonly List<TrackLaneViewModel> _trackLanes = new();
        private LaneViewModel? _selectedLane;
        private bool _syncingSelection;

        public TimelineViewModel(IProjectService project, ISelectionService selection,
            IEventAggregator events, IAudioFileService audioFiles, ITransportService transport,
            IInstrumentRegistry instruments, IEditModeService editMode, IRecordingService recording,
            Services.IHistoryService history)
        {
            _project = project;
            _selection = selection;
            _events = events;
            _audioFiles = audioFiles;
            _transport = transport;
            _instruments = instruments;
            _editMode = editMode;
            _recording = recording;
            _history = history;

            SelectClipCommand = new RelayCommand<ClipViewModel>(OnClipClicked);
            AddInstrumentTrackCommand = new RelayCommand(AddInstrumentTrack);
            AddAudioTrackCommand = new RelayCommand(AddAudioTrack);

            _project.ProjectChanged += Rebuild;
            _selection.SelectionChanged += OnSelectionChanged;
            events.Subscribe<TrackChangedEvent>(e => RefreshTrack(e.Track));
            events.Subscribe<ClipChangedEvent>(e => RefreshClip(e.Clip));
            events.Subscribe<ClipNotesChangedEvent>(e => RefreshClipNotes(e.Clip));
            events.Subscribe<ClipAddedEvent>(e => OnClipAdded(e.Track, e.Clip));
            events.Subscribe<AutomationChangedEvent>(e => OnAutomationChanged(e.Track));
            events.Subscribe<ArrangementLengthChangedEvent>(_ => ResizeArrangement());
            _transport.StateChanged += _ => OnPropertyChanged(nameof(IsPlaying));
            _transport.StartBeatChanged += () => OnPropertyChanged(nameof(StartBeat));
            // Re-fit tempo-synced audio clips to the grid whenever the project tempo changes.
            _transport.TempoChanged += _ => OnProjectTempoChanged();
            _editMode.ModeChanged += () =>
            {
                OnPropertyChanged(nameof(IsSelectMode));
                OnPropertyChanged(nameof(IsSliceMode));
            };

            Rebuild();
        }

        /// <summary>True when Select (rubber-band multi-select) mode is active.</summary>
        public bool IsSelectMode => _editMode.Mode == EditMode.Select;

        /// <summary>True when Slice mode (click a clip to cut it) is active.</summary>
        public bool IsSliceMode => _editMode.Mode == EditMode.Slice;

        /// <summary>Raises the per-lane meter levels (called from the view's meter timer).</summary>
        public void RefreshMeters()
        {
            foreach (var lane in _trackLanes) lane.RaiseMeter();
        }

        /// <summary>
        /// Pumps the live recording take (called from the view's frame timer): grows the take clip
        /// and its notes to the playhead, keeps the ruler long enough to show them, and repaints any
        /// armed automation rows as their captured points fill in.
        /// </summary>
        public void RefreshRecording()
        {
            if (!_recording.IsRecording) return;
            _recording.RefreshLive();
            ExtendTimeline(_transport.PlayheadBeats);
            foreach (var row in Lanes)
            {
                if (row is AutomationLaneViewModel auto) auto.BumpRevision();
            }
        }

        /// <summary>Whether the transport is playing (drives the playhead timer in the view).</summary>
        public bool IsPlaying => _transport.State == TransportState.Playing;

        /// <summary>Current playhead position in beats (polled by the view while playing).</summary>
        public double PlayheadBeats => _transport.PlayheadBeats;

        /// <summary>Playback rate in beats per second — lets the view interpolate the playhead smoothly
        /// between the engine's (per-audio-block) position reports.</summary>
        public double BeatsPerSecond => _transport.Tempo.BeatsPerMinute / 60.0;

        /// <summary>The start marker position in beats.</summary>
        public double StartBeat => _transport.StartBeat;

        /// <summary>Loop region start/end in beats, and whether a region is active (drawn on the ruler).</summary>
        public double LoopStart => _transport.LoopStart;
        public double LoopEnd => _transport.LoopEnd;
        public bool IsLoopActive => _transport.IsLoopActive;

        /// <summary>Sets the start marker to the given beat, snapped to the nearest bar.</summary>
        public void SetStartBeat(double beat)
        {
            var bar = BeatsPerBar;
            _transport.StartBeat = Math.Max(0, Math.Round(beat / bar) * bar);
        }

        /// <summary>Beats per bar from the project's time signature.</summary>
        public int BeatsPerBar => Math.Max(1, _project.Current.TimeSignature.Numerator);

        /// <summary>Whether a file path is an audio file the timeline can ingest (drag-and-drop gate).</summary>
        public bool CanIngest(string path) => _audioFiles.IsAudioFile(path);

        /// <summary>Selects a clip (used by the timeline pointer gestures).</summary>
        public void SelectClip(ClipViewModel clip) => _selection.SelectClip(clip.Model, clip.Owner);

        /// <summary>Selects the track owning the row at <paramref name="rowIndex"/>, clearing any clip selection.</summary>
        public void SelectTrackAtRow(int rowIndex)
        {
            var lane = TrackLaneAtRow(rowIndex);
            if (lane is not null) _selection.SelectTrack(lane.Model);
        }

        /// <summary>Selects a lane's track, clearing any clip selection.</summary>
        public void SelectLane(TrackLaneViewModel lane) => _selection.SelectTrack(lane.Model);

        /// <summary>Ctrl+click: toggles a lane's track in the multi-selection (for grouping).</summary>
        public void ToggleLaneSelection(TrackLaneViewModel lane) => _selection.ToggleTrackSelection(lane.Model);

        /// <summary>
        /// Right-click selection: if the lane is already part of the multi-selection, leave it intact (so
        /// "Group tracks" still sees all of them); otherwise select just this lane.
        /// </summary>
        public void EnsureContextSelection(TrackLaneViewModel lane)
        {
            if (!_selection.SelectedTracks.Contains(lane.Model)) _selection.SelectTrack(lane.Model);
        }

        /// <summary>Selects a track directly (e.g. when an automation header is clicked).</summary>
        public void SelectTrack(Track track) => _selection.SelectTrack(track);

        /// <summary>The lane view model that currently holds <paramref name="clip"/>, or null.</summary>
        public TrackLaneViewModel? FindLaneOf(ClipViewModel clip)
            => _trackLanes.FirstOrDefault(l => l.Clips.Contains(clip));

        /// <summary>Creates an empty 1-bar MIDI clip on the (instrument) row at <paramref name="beat"/>, bar-snapped, and selects it.</summary>
        public void CreateMidiClip(int rowIndex, double beat)
        {
            var lane = TrackLaneAtRow(rowIndex);
            if (lane is null || lane.Model.Kind != TrackKind.Instrument) return;
            _history.Capture("Create MIDI clip");

            var bar = BeatsPerBar;
            var start = Math.Max(0, Math.Floor(beat / bar) * bar);
            var clip = new Clip { Name = "MIDI", StartBeat = start, LengthBeats = bar };

            lane.Model.Clips.Add(clip);
            lane.Clips.Add(new ClipViewModel(clip, lane.Model, Metrics, this));
            ExtendTimeline(clip.EndBeat);
            _selection.SelectClip(clip, lane.Model);
        }

        /// <summary>Applies a live geometry change (move/resize) to a clip and notifies other views.</summary>
        public void NotifyClipGeometryChanged(ClipViewModel clip)
        {
            clip.RefreshFromModel();
            ExtendTimeline(clip.Model.EndBeat);
            _events.Publish(new ClipChangedEvent(clip.Model));
        }

        /// <summary>
        /// Slices the clip in two at <paramref name="sliceBeat"/> (an absolute timeline beat, expected
        /// grid-snapped by the caller). The existing clip becomes the left piece; a new clip is added for
        /// the right piece. MIDI notes are partitioned (and notes straddling the cut are split); audio
        /// pieces reference windows of the same source buffer so each plays only its portion. No-op if the
        /// cut doesn't fall strictly inside the clip.
        /// </summary>
        public void SliceClip(ClipViewModel clipVm, double sliceBeat)
        {
            var lane = FindLaneOf(clipVm);
            if (lane is null) return;

            var model = clipVm.Model;
            const double eps = 1e-6;
            if (sliceBeat <= model.StartBeat + eps || sliceBeat >= model.EndBeat - eps) return;
            _history.Capture("Slice clip");

            var origLength = model.LengthBeats;
            var leftLen = sliceBeat - model.StartBeat;
            var rightLen = model.EndBeat - sliceBeat;

            var right = new Clip
            {
                Name = model.Name,
                StartBeat = sliceBeat,
                LengthBeats = rightLen,
                IsAudio = model.IsAudio,
                StretchToTempo = model.StretchToTempo,
                SourceTempo = model.SourceTempo,
                AudioFilePath = model.AudioFilePath,
                Waveform = model.Waveform,
                Samples = model.Samples
            };

            if (model.IsAudio)
            {
                // Split the source window so each piece reads only its part of the buffer. Source seconds
                // map linearly to clip beats (constant playback rate within a clip), for both stretched
                // and native clips.
                var fullDur = model.Samples is { } s && s.SampleRate > 0
                    ? s.FrameCount / (double)s.SampleRate
                    : 0.0;
                var sourceLen = model.SourceLengthSeconds ?? Math.Max(0.0, fullDur - model.SourceOffsetSeconds);
                var leftSourceLen = origLength > 0 ? sourceLen * (leftLen / origLength) : 0.0;

                right.SourceOffsetSeconds = model.SourceOffsetSeconds + leftSourceLen;
                right.SourceLengthSeconds = sourceLen - leftSourceLen;
                model.SourceLengthSeconds = leftSourceLen;
            }
            else
            {
                // Partition notes around the cut (clip-relative). A note crossing the boundary is split so
                // each piece keeps the part that falls within it.
                var leftNotes = new List<MidiNote>();
                foreach (var n in model.Notes)
                {
                    if (n.EndBeat <= leftLen + eps)
                    {
                        leftNotes.Add(n);
                    }
                    else if (n.StartBeat >= leftLen - eps)
                    {
                        right.Notes.Add(new MidiNote
                        {
                            Note = n.Note,
                            StartBeat = n.StartBeat - leftLen,
                            LengthBeats = n.LengthBeats,
                            Velocity = n.Velocity
                        });
                    }
                    else
                    {
                        leftNotes.Add(new MidiNote
                        {
                            Note = n.Note,
                            StartBeat = n.StartBeat,
                            LengthBeats = leftLen - n.StartBeat,
                            Velocity = n.Velocity
                        });
                        right.Notes.Add(new MidiNote
                        {
                            Note = n.Note,
                            StartBeat = 0,
                            LengthBeats = n.EndBeat - leftLen,
                            Velocity = n.Velocity
                        });
                    }
                }

                model.Notes.Clear();
                model.Notes.AddRange(leftNotes);
            }

            model.LengthBeats = leftLen;

            lane.Model.Clips.Add(right);
            lane.Clips.Add(new ClipViewModel(right, lane.Model, Metrics, this));

            clipVm.RefreshFromModel();
            _events.Publish(new ClipChangedEvent(model));
            _selection.SelectClip(model, lane.Model);
        }

        // --- IClipActions (context menu) ---

        /// <summary>
        /// Duplicates the currently selected clip (Ctrl+D). The copy lands one clip-length to the right and
        /// becomes the new selection, so repeated presses lay copies end-to-end down the timeline.
        /// </summary>
        public void DuplicateSelectedClip()
        {
            var selected = _selection.SelectedClip;
            if (selected is null) return;
            foreach (var lane in _trackLanes)
            {
                var clipVm = lane.Clips.FirstOrDefault(c => ReferenceEquals(c.Model, selected));
                if (clipVm is not null)
                {
                    DuplicateClip(clipVm);
                    return;
                }
            }
        }

        public void DuplicateClip(ClipViewModel clip)
        {
            var lane = FindLaneOf(clip);
            if (lane is null) return;
            _history.Capture("Duplicate clip");
            var copy = CloneClip(clip.Model);
            copy.StartBeat = clip.Model.StartBeat + clip.Model.LengthBeats;
            lane.Model.Clips.Add(copy);
            lane.Clips.Add(new ClipViewModel(copy, lane.Model, Metrics, this));
            ExtendTimeline(copy.EndBeat);
            _selection.SelectClip(copy, lane.Model);
        }

        /// <summary>
        /// Reverses an audio clip's playback by baking a frame-reversed copy of just this clip's source
        /// window into a fresh buffer. Only this clip is affected — other clips sharing the same source
        /// (e.g. slices or duplicates) keep playing forwards. Reversing again restores the original order.
        /// </summary>
        public void ReverseClip(ClipViewModel clip)
        {
            var m = clip.Model;
            if (!m.IsAudio || m.Samples is not { } samples || samples.FrameCount <= 0) return;

            var channels = samples.Channels;
            var sampleRate = samples.SampleRate;
            var totalFrames = samples.FrameCount;
            var fullDuration = totalFrames / (double)sampleRate;

            // The portion of the source this clip currently plays (whole buffer for an un-sliced clip).
            var wasWindowed = m.SourceLengthSeconds is not null;
            var windowSeconds = m.SourceLengthSeconds ?? Math.Max(0.0, fullDuration - m.SourceOffsetSeconds);

            var startFrame = (long)Math.Round(m.SourceOffsetSeconds * sampleRate);
            var windowFrames = (long)Math.Round(windowSeconds * sampleRate);
            if (startFrame < 0) startFrame = 0;
            if (windowFrames <= 0 || startFrame >= totalFrames) return;
            if (startFrame + windowFrames > totalFrames) windowFrames = totalFrames - startFrame;
            _history.Capture("Reverse clip");

            // Copy the clip's window frame-reversed (channels preserved within each frame) into a new buffer.
            var src = samples.Samples;
            var reversed = new float[windowFrames * channels];
            for (long i = 0; i < windowFrames; i++)
            {
                var srcBase = (startFrame + windowFrames - 1 - i) * channels;
                var dstBase = i * channels;
                for (var c = 0; c < channels; c++) reversed[dstBase + c] = src[srcBase + c];
            }

            var reversedBuffer = new AudioSampleBuffer(reversed, channels, sampleRate);
            m.Samples = reversedBuffer;
            m.Waveform = AudioWaveform.Build(reversedBuffer);
            m.SourceOffsetSeconds = 0;
            // The new buffer is exactly the window; preserve the slice/whole distinction so tempo re-fitting
            // still behaves the same (frozen for slices, octave-snapped for whole loops).
            m.SourceLengthSeconds = wasWindowed ? reversedBuffer.FrameCount / (double)sampleRate : null;

            clip.RefreshFromModel();
            _events.Publish(new ClipChangedEvent(m));
        }

        public void DeleteClip(ClipViewModel clip)
        {
            var lane = FindLaneOf(clip);
            if (lane is null) return;
            _history.Capture("Delete clip");
            lane.Model.Clips.Remove(clip.Model);
            lane.Clips.Remove(clip);
            if (ReferenceEquals(_selection.SelectedClip, clip.Model)) _selection.SelectTrack(lane.Model);
        }

        // --- Rubber-band multi-select (Select mode) ---

        /// <summary>Selects every clip intersecting the rectangle (content pixel coordinates).</summary>
        public void SelectClipsInRect(double x0, double y0, double x1, double y1)
        {
            var minX = Math.Min(x0, x1);
            var maxX = Math.Max(x0, x1);
            var minY = Math.Min(y0, y1);
            var maxY = Math.Max(y0, y1);

            var top = 0.0;
            for (var i = 0; i < Lanes.Count; i++)
            {
                var height = Lanes[i].Height;
                if (Lanes[i] is TrackLaneViewModel track)
                {
                    var laneOverlaps = top < maxY && top + height > minY;
                    foreach (var clip in track.Clips)
                    {
                        clip.IsSelected = laneOverlaps && clip.Left < maxX && clip.Left + clip.Width > minX;
                    }
                }

                top += height;
            }
        }

        /// <summary>Deletes all clips currently marked selected.</summary>
        public void DeleteSelectedClips()
        {
            if (!_trackLanes.Any(l => l.Clips.Any(c => c.IsSelected))) return;
            _history.Capture("Delete clips");
            foreach (var lane in _trackLanes)
            {
                var selected = lane.Clips.Where(c => c.IsSelected).ToList();
                foreach (var clip in selected)
                {
                    lane.Model.Clips.Remove(clip.Model);
                    lane.Clips.Remove(clip);
                    if (ReferenceEquals(_selection.SelectedClip, clip.Model)) _selection.SelectTrack(null);
                }
            }
        }

        /// <summary>
        /// Moves a MIDI clip to the instrument track lane <paramref name="target"/>.
        /// Returns the clip's new view model, or null if the move isn't allowed.
        /// </summary>
        public ClipViewModel? TryReparentClip(ClipViewModel clip, TrackLaneViewModel? target)
        {
            if (target is null) return null;
            var origin = FindLaneOf(clip);
            if (origin is null || ReferenceEquals(origin, target)) return null;

            // Clips can only move between tracks of a compatible kind: audio clips onto audio tracks,
            // MIDI clips onto instrument tracks. Buses (group/master) carry no clips.
            var kind = target.Model.Kind;
            var compatible = clip.Model.IsAudio ? kind == TrackKind.Audio : kind == TrackKind.Instrument;
            if (!compatible) return null;

            origin.Model.Clips.Remove(clip.Model);
            origin.Clips.Remove(clip);

            target.Model.Clips.Add(clip.Model);
            var moved = new ClipViewModel(clip.Model, target.Model, Metrics, this);
            target.Clips.Add(moved);

            _selection.SelectClip(clip.Model, target.Model);
            return moved;
        }

        /// <summary>
        /// The rows shown in the arrange view: each track lane followed by its (expanded) automation
        /// rows. Polymorphic — <see cref="TrackLaneViewModel"/> or <see cref="AutomationLaneViewModel"/>.
        /// </summary>
        public ObservableCollection<LaneViewModel> Lanes { get; } = new();

        /// <summary>Bar markers for the ruler.</summary>
        public ObservableCollection<BarTickViewModel> Bars { get; } = new();

        /// <summary>Time&lt;-&gt;pixel mapping shared by every lane, clip, and ruler tick.</summary>
        public TimelineMetrics Metrics { get; } = new();

        /// <summary>Bound to the lane ListBox's SelectedItem; reports track selection.</summary>
        public LaneViewModel? SelectedLane
        {
            get => _selectedLane;
            set
            {
                if (!SetField(ref _selectedLane, value)) return;
                if (_syncingSelection) return;
                var track = value switch
                {
                    TrackLaneViewModel t => t.Model,
                    AutomationLaneViewModel a => a.OwnerTrack,
                    _ => null
                };
                _selection.SelectTrack(track);
            }
        }

        public RelayCommand<ClipViewModel> SelectClipCommand { get; }

        /// <summary>Add-track commands for the timeline's blank-area context menu.</summary>
        public RelayCommand AddInstrumentTrackCommand { get; }
        public RelayCommand AddAudioTrackCommand { get; }

        private void OnClipClicked(ClipViewModel? clip)
        {
            if (clip is null) return;
            _selection.SelectClip(clip.Model, clip.Owner);
        }

        // --- Variable-height row geometry (cumulative, since rows differ in height) ---

        /// <summary>Pixel Y of the top of the row at <paramref name="index"/> (index == count → bottom of all rows).</summary>
        public double RowTop(int index)
        {
            var y = 0.0;
            var n = Math.Min(index, Lanes.Count);
            for (var i = 0; i < n; i++) y += Lanes[i].Height;
            return y;
        }

        /// <summary>Total stacked height of all rows.</summary>
        public double RowsTotalHeight => RowTop(Lanes.Count);

        /// <summary>Index of the row containing content-Y <paramref name="y"/>; returns the row count if below all rows.</summary>
        public int RowIndexAtY(double y)
        {
            if (y < 0) return 0;
            var top = 0.0;
            for (var i = 0; i < Lanes.Count; i++)
            {
                var h = Lanes[i].Height;
                if (y < top + h) return i;
                top += h;
            }

            return Lanes.Count;
        }

        /// <summary>The track lane owning the row at <paramref name="rowIndex"/> (the row itself if it's a track, else the automation row's owner).</summary>
        public TrackLaneViewModel? TrackLaneAtRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= Lanes.Count) return null;
            return Lanes[rowIndex] switch
            {
                TrackLaneViewModel t => t,
                AutomationLaneViewModel a => _trackLanes.FirstOrDefault(l => ReferenceEquals(l.Model, a.OwnerTrack)),
                _ => null
            };
        }

        /// <summary>True when the row at <paramref name="rowIndex"/> is an automation row.</summary>
        public bool IsAutomationRow(int rowIndex)
            => rowIndex >= 0 && rowIndex < Lanes.Count && Lanes[rowIndex] is AutomationLaneViewModel;

        /// <summary>Track-list insertion index for a drop on the row at <paramref name="rowIndex"/> (count → append).</summary>
        public int TrackInsertIndexForRow(int rowIndex)
        {
            var lane = TrackLaneAtRow(rowIndex);
            return lane is null ? _trackLanes.Count : _trackLanes.IndexOf(lane);
        }

        /// <summary>
        /// When content-Y is within <paramref name="band"/> px of the line between two track rows (below the
        /// master, above the bottom append zone), returns that boundary's track-insert index and its pixel Y.
        /// Returns (-1, 0) when the pointer is not near an inter-track boundary — used to drop a dragged audio
        /// file as a brand-new track inserted in place rather than onto an existing lane.
        /// </summary>
        public (int InsertIndex, double IndicatorY) HitTrackBoundary(double y, double band)
        {
            // Internal boundaries are the tops of rows 1..Count-1 (row 0's top sits above the pinned master;
            // the bottom edge is already the "append new track" affordance handled by the ghost).
            for (var i = 1; i < Lanes.Count; i++)
            {
                var top = RowTop(i);
                if (Math.Abs(y - top) <= band)
                    return (TrackInsertIndexForRow(i), top);
            }

            return (-1, 0);
        }

        /// <summary>Number of rendered rows (used by the view for layout/hit-testing).</summary>
        public int RowCount => Lanes.Count;

        /// <summary>Number of track lanes.</summary>
        public int LaneCount => _trackLanes.Count;

        /// <summary>Highlights the drop-target track lane (null clears all — e.g. for a new track).</summary>
        public void SetDropHighlight(TrackLaneViewModel? target)
        {
            foreach (var lane in _trackLanes) lane.IsDropTarget = ReferenceEquals(lane, target);
        }

        /// <summary>Clears any drop highlight.</summary>
        public void ClearDropHighlight() => SetDropHighlight((TrackLaneViewModel?)null);

        /// <summary>
        /// Inserts an audio clip from <paramref name="path"/> at <paramref name="beat"/> on the
        /// lane at <paramref name="laneIndex"/>, or on a new audio track if the index is past the
        /// last lane. Decoding runs off the UI thread; the clip appears once decoded.
        /// </summary>
        public async void AddAudioClip(string path, TrackLaneViewModel? target, double beat, int newTrackIndex = -1)
        {
            ClearDropHighlight();
            var startBeat = Math.Max(0, Math.Round(beat));

            // Decode off the UI thread; await resumes on the UI thread (Avalonia sync context).
            var loaded = await Task.Run(() =>
            {
                try { return _audioFiles.Load(path); }
                catch { return null; }
            });

            _history.Capture("Add audio clip");
            InsertClip(path, target, startBeat, loaded, newTrackIndex);
        }

        // newTrackIndex >= 0 forces a NEW audio track inserted at that track index (drop between two tracks);
        // otherwise the clip lands on `target` if given, else a new track appended at the end.
        private void InsertClip(string path, TrackLaneViewModel? target, double startBeat,
            Core.Audio.Files.LoadedAudio? loaded, int newTrackIndex = -1)
        {
            // Buses (group/master) hold no clips — a drop on one creates a new audio track instead.
            if (target is { Model.IsBus: true }) target = null;

            var project = _project.Current;
            // Use the live transport tempo (what the user sets), so an N-bar sample lands on N bars.
            var bpm = _transport.Tempo.BeatsPerMinute;
            var duration = loaded?.Waveform.DurationSeconds ?? 0;

            // Default: place the clip at its native duration (no stretch).
            var lengthBeats = duration > 0 ? duration * bpm / 60.0 : 4.0;
            var stretchToTempo = false;
            double? sourceTempo = null;

            // If we know the sample's natural tempo, fit it to the grid and time-stretch to match.
            // Tagged (name-based) tempos always sync; estimated tempos only for loop-length material
            // (≥ ~2 beats) so we don't warp one-shots on a shaky estimate.
            if (duration > 0 && loaded?.Tempo is { } naturalBpm && naturalBpm > 0)
            {
                var naturalBeats = duration * naturalBpm / 60.0;
                if (loaded.TempoFromName || naturalBeats >= 2.0)
                {
                    var musical = Core.Audio.TempoSync.MusicalBeats(duration, naturalBpm, bpm);
                    if (musical > 0)
                    {
                        lengthBeats = musical;
                        stretchToTempo = true;
                        sourceTempo = naturalBpm;
                    }
                }
            }

            var clip = new Clip
            {
                Name = Path.GetFileNameWithoutExtension(path),
                StartBeat = startBeat,
                LengthBeats = lengthBeats,
                IsAudio = true,
                StretchToTempo = stretchToTempo,
                SourceTempo = sourceTempo,
                AudioFilePath = path,
                Waveform = loaded?.Waveform,
                Samples = loaded?.Samples
            };

            TrackLaneViewModel lane;
            if (target is not null)
            {
                lane = target;
                lane.Model.Clips.Add(clip);
                lane.Clips.Add(new ClipViewModel(clip, lane.Model, Metrics, this));
            }
            else
            {
                var track = new Track
                {
                    Name = $"Audio {AudioTrackNumber()}",
                    Kind = TrackKind.Audio,
                    ColorKey = "CatppuccinTeal"
                };
                track.Clips.Add(clip);

                if (newTrackIndex >= 0)
                {
                    // Drop between two tracks: insert the new track in place (InsertTrack rebuilds + publishes).
                    InsertTrack(track, newTrackIndex);
                    lane = _trackLanes.FirstOrDefault(l => ReferenceEquals(l.Model, track)) ?? _trackLanes[^1];
                }
                else
                {
                    project.Tracks.Add(track);
                    lane = new TrackLaneViewModel(track, Metrics, this, this); // ctor builds the clip's view model
                    _trackLanes.Add(lane);
                    RebuildRows();
                    OnPropertyChanged(nameof(LaneCount));
                    _events.Publish(new TracksChangedEvent()); // let the engine pick up the new track
                }
            }

            ExtendTimeline(clip.EndBeat);
            _selection.SelectClip(clip, lane.Model);
        }

        private int AudioTrackNumber()
            => _trackLanes.Count(l => l.Model.Kind == TrackKind.Audio) + 1;

        /// <summary>Grows the arrange length (and ruler) so it covers <paramref name="endBeat"/>.</summary>
        private void ExtendTimeline(double endBeat)
        {
            var beatsPerBar = Math.Max(1, _project.Current.TimeSignature.Numerator);
            var neededBars = (int)Math.Ceiling((endBeat + beatsPerBar * 4) / beatsPerBar);
            if (neededBars <= Bars.Count) return;

            for (var bar = Bars.Count; bar < neededBars; bar++)
            {
                Bars.Add(new BarTickViewModel(bar + 1, Metrics));
            }

            Metrics.TotalBeats = neededBars * beatsPerBar;
        }

        private void Rebuild()
        {
            _trackLanes.Clear();
            foreach (var track in _project.Current.Tracks)
            {
                _trackLanes.Add(new TrackLaneViewModel(track, Metrics, this, this));
            }

            RecomputeIndents();
            RebuildRows();
            ResizeArrangement();
        }

        // Sets each lane's nesting depth and colour rails from its parent chain so headers indent under
        // their group and each group's colour forms a continuous rail down through its descendants.
        private void RecomputeIndents()
        {
            foreach (var lane in _trackLanes)
            {
                lane.IndentLevel = DepthOf(lane.Model);
                lane.GutterBars = BuildGutterBars(lane.Model);
            }
        }

        // Outermost ancestor group's colour first, then inward, then the track's own colour last.
        private IReadOnlyList<Timeline.LaneGutterBar> BuildGutterBars(Track track)
        {
            var ancestors = new List<Timeline.LaneGutterBar>();
            var pid = track.ParentId;
            var guard = 0;
            while (pid is { } id && guard++ < 256)
            {
                var parent = _project.Current.Tracks.FirstOrDefault(x => x.Id == id);
                if (parent is null) break;
                ancestors.Insert(0, new Timeline.LaneGutterBar(parent.ColorKey)); // outermost ends up first
                pid = parent.ParentId;
            }

            ancestors.Add(new Timeline.LaneGutterBar(track.ColorKey)); // this row's own colour, deepest rail
            return ancestors;
        }

        // Number of group ancestors above a track (0 = top level). The master is the implicit root and is
        // never itself a ParentId, so top-level tracks/groups are depth 0.
        private int DepthOf(Track track)
        {
            var depth = 0;
            var pid = track.ParentId;
            var guard = 0;
            while (pid is { } id && guard++ < 256)
            {
                var parent = _project.Current.Tracks.FirstOrDefault(x => x.Id == id);
                if (parent is null) break;
                depth++;
                pid = parent.ParentId;
            }

            return depth;
        }

        // Rebuilds the rendered <see cref="Lanes"/> from the track lanes in flattened-tree order: each row
        // followed by its automation rows, skipping the subtree of any collapsed group. The same track-lane
        // instances are reused so clip state survives; only the automation rows are fresh.
        private void RebuildRows()
        {
            Lanes.Clear();
            var collapsedDepth = int.MaxValue; // while a group is collapsed, hide everything deeper than it
            foreach (var trackLane in _trackLanes)
            {
                var depth = trackLane.IndentLevel;
                if (depth > collapsedDepth) continue;
                collapsedDepth = int.MaxValue;

                Lanes.Add(trackLane);
                trackLane.RefreshAutomationState();

                if (trackLane.IsGroup && trackLane.Model.GroupCollapsed)
                {
                    collapsedDepth = depth; // hide this group's children and automation
                    continue;
                }

                if (trackLane.Model.AutomationCollapsed) continue;
                foreach (var auto in trackLane.Model.AutoLanes)
                {
                    // The owning track's rails continue down, plus one more rail in the track's colour so the
                    // automation row is indented one level under it (no dark divider rail).
                    var autoBars = new List<Timeline.LaneGutterBar>(trackLane.GutterBars)
                    {
                        new(trackLane.Model.ColorKey)
                    };
                    Lanes.Add(new AutomationLaneViewModel(trackLane.Model, auto, Metrics, this)
                    {
                        IndentWidth = (trackLane.IndentLevel + 1) * 16.0,
                        GutterBars = autoBars
                    });
                }
            }

            OnPropertyChanged(nameof(RowCount));
        }

        // A track's automation lanes were added/removed/collapsed elsewhere: rebuild the rendered rows.
        private void OnAutomationChanged(Core.Models.Audio.Track track)
        {
            var lane = _trackLanes.FirstOrDefault(l => ReferenceEquals(l.Model, track));
            lane?.RefreshAutomationState();
            RebuildRows();
        }

        // Project tempo changed: re-fit each tempo-synced audio clip to the grid. Its musical length in
        // beats is recomputed (re-snapping the half/double octave) so the loop stays aligned; the engine
        // then resamples the audio to span that beat-length at the new tempo on the next playback.
        private void OnProjectTempoChanged()
        {
            var projBpm = _transport.Tempo.BeatsPerMinute;
            if (projBpm <= 0) return;

            foreach (var lane in _trackLanes)
            {
                foreach (var clipVm in lane.Clips)
                {
                    var m = clipVm.Model;
                    if (!m.StretchToTempo || m.Samples is null || m.SourceTempo is not { } source) continue;

                    // A sliced clip carries an explicit source window: keep its grid length fixed and let
                    // the engine resample just that window to fit at the new tempo (so a 1-bar slice stays
                    // 1 bar). Only whole loops get their musical length re-snapped by octaves.
                    if (m.SourceLengthSeconds is not null) continue;

                    var duration = m.Samples.FrameCount / (double)m.Samples.SampleRate;
                    var beats = Core.Audio.TempoSync.MusicalBeats(duration, source, projBpm);
                    if (beats <= 0 || Math.Abs(beats - m.LengthBeats) < 1e-6) continue;

                    m.LengthBeats = beats;
                    clipVm.RefreshFromModel();
                    _events.Publish(new ClipChangedEvent(m));
                    ExtendTimeline(m.EndBeat);
                }
            }
        }

        // Sizes the ruler/arrange area to max(project bar count, content) bars.
        private void ResizeArrangement()
        {
            var project = _project.Current;
            var beatsPerBar = BeatsPerBar;
            var contentEnd = project.Tracks
                .SelectMany(t => t.Clips)
                .Select(c => c.EndBeat)
                .DefaultIfEmpty(0)
                .Max();
            var contentBars = (int)Math.Ceiling(contentEnd / beatsPerBar);
            var bars = Math.Max(project.BarCount, contentBars);

            Metrics.BeatsPerBar = beatsPerBar;
            Metrics.TotalBeats = bars * beatsPerBar;

            if (Bars.Count > bars)
            {
                while (Bars.Count > bars) Bars.RemoveAt(Bars.Count - 1);
            }
            else
            {
                for (var bar = Bars.Count; bar < bars; bar++)
                {
                    Bars.Add(new BarTickViewModel(bar + 1, Metrics));
                }
            }
        }

        // --- ITrackActions ---

        public void DuplicateTrack(TrackLaneViewModel lane)
        {
            if (lane.Model.IsBus) return; // groups/master aren't duplicated
            _history.Capture("Duplicate track");

            var src = lane.Model;
            var copy = new Track
            {
                Name = src.Name + " copy",
                Kind = src.Kind,
                ParentId = src.ParentId, // stay in the same group
                IsMuted = src.IsMuted,
                IsSoloed = src.IsSoloed,
                Volume = src.Volume,
                Pan = src.Pan,
                ColorKey = src.ColorKey
            };

            // Clone the instrument rack: each slot's instrument + its own effect chain, preserving bypass.
            foreach (var slot in src.Instruments)
            {
                var copySlot = new Core.Models.Audio.InstrumentSlot(slot.Instrument.Clone()) { Enabled = slot.Enabled };
                foreach (var fx in slot.Effects) copySlot.Effects.Add(fx.Clone());
                copySlot.CommitEffects();
                copy.Instruments.Add(copySlot);
            }

            copy.CommitInstruments();
            foreach (var clip in src.Clips)
            {
                copy.Clips.Add(CloneClip(clip));
            }

            var index = _project.Current.Tracks.IndexOf(src);
            InsertTrack(copy, index < 0 ? _trackLanes.Count : index + 1);
        }

        public void DeleteTrack(TrackLaneViewModel lane)
        {
            if (lane.Model.Kind == TrackKind.Master) return;             // the master can't be deleted
            if (lane.Model.Kind == TrackKind.Group) { DeleteGroupKeepChildren(lane); return; } // default: ungroup
            _history.Capture("Delete track");

            var wasSelected = ReferenceEquals(_selection.SelectedTrack, lane.Model);
            _project.Current.Tracks.Remove(lane.Model);
            _trackLanes.Remove(lane);
            RebuildRows();
            OnPropertyChanged(nameof(LaneCount));
            if (wasSelected) _selection.SelectTrack(null);
            _events.Publish(new TracksChangedEvent());
        }

        public void ToggleAutomation(TrackLaneViewModel lane)
        {
            lane.AutomationCollapsed = !lane.AutomationCollapsed;
            RebuildRows();
        }

        public void ToggleGroup(TrackLaneViewModel lane)
        {
            if (lane.Model.Kind != TrackKind.Group) return;
            lane.GroupCollapsed = !lane.GroupCollapsed;
            RebuildRows();
        }

        /// <summary>Groups the multi-selected tracks under a new group bus placed where the first one was.</summary>
        public void GroupSelectedTracks()
        {
            var tracks = _project.Current.Tracks;
            var selected = new HashSet<Guid>(_selection.SelectedTracks
                .Where(t => t.Kind != TrackKind.Master).Select(t => t.Id));
            if (selected.Count == 0) return;

            // Keep only the roots of the selection (drop any whose ancestor is also selected).
            var roots = tracks.Where(t => selected.Contains(t.Id) && !HasSelectedAncestor(t, selected)).ToList();
            if (roots.Count == 0) return;
            _history.Capture("Group tracks");

            var group = new Track
            {
                Name = "Group",
                Kind = TrackKind.Group,
                ColorKey = "CatppuccinBlue",
                Volume = 1.0,
                ParentId = roots[0].ParentId // sit under the first selected track's parent
            };

            // Gather each root's subtree (root + descendants) in document order.
            var moved = new List<Track>();
            foreach (var root in roots) moved.AddRange(SubtreeOf(root, tracks));

            var insertAt = tracks.IndexOf(moved[0]);
            foreach (var t in moved) tracks.Remove(t);
            insertAt = Math.Clamp(insertAt, 0, tracks.Count);

            foreach (var root in roots) root.ParentId = group.Id;
            tracks.Insert(insertAt, group);
            tracks.InsertRange(insertAt + 1, moved);

            _events.Publish(new TracksChangedEvent());
            Rebuild();
            _selection.SelectTrack(group);
        }

        /// <summary>Removes a group, moving its direct children up to the group's own parent (ungroup).</summary>
        public void DeleteGroupKeepChildren(TrackLaneViewModel lane)
        {
            var group = lane.Model;
            if (group.Kind != TrackKind.Group) return;
            _history.Capture("Ungroup");

            var tracks = _project.Current.Tracks;
            foreach (var t in tracks.Where(t => t.ParentId == group.Id)) t.ParentId = group.ParentId;
            tracks.Remove(group);

            if (ReferenceEquals(_selection.SelectedTrack, group)) _selection.SelectTrack(null);
            _events.Publish(new TracksChangedEvent());
            Rebuild();
            OnPropertyChanged(nameof(LaneCount));
        }

        /// <summary>Removes a group and every track nested inside it.</summary>
        public void DeleteGroupAndChildren(TrackLaneViewModel lane)
        {
            var group = lane.Model;
            if (group.Kind != TrackKind.Group) return;
            _history.Capture("Delete group");

            var tracks = _project.Current.Tracks;
            var doomed = tracks.Where(t => ReferenceEquals(t, group) || IsDescendantOf(t, group.Id, tracks)).ToList();
            var clearedSelection = doomed.Any(t => ReferenceEquals(t, _selection.SelectedTrack));
            foreach (var t in doomed) tracks.Remove(t);

            if (clearedSelection) _selection.SelectTrack(null);
            _events.Publish(new TracksChangedEvent());
            Rebuild();
            OnPropertyChanged(nameof(LaneCount));
        }

        /// <summary>Moves a track one level out of its group: it pops out as a sibling just below the group.</summary>
        public void DetachFromGroup(TrackLaneViewModel lane)
        {
            var track = lane.Model;
            if (track.ParentId is not { } pid) return; // already top level

            var tracks = _project.Current.Tracks;
            var parent = tracks.FirstOrDefault(x => x.Id == pid);
            if (parent is null) return;
            _history.Capture("Detach from group");

            // Lift the track's whole subtree out and reinsert it right after the old group's subtree.
            var subtree = SubtreeOf(track, tracks);
            foreach (var t in subtree) tracks.Remove(t);

            track.ParentId = parent.ParentId; // one level out
            var parentSubtree = SubtreeOf(parent, tracks); // parent's remaining descendants
            var insertAt = tracks.IndexOf(parentSubtree[^1]) + 1;
            tracks.InsertRange(insertAt, subtree);

            _events.Publish(new TracksChangedEvent());
            Rebuild();
            _selection.SelectTrack(track);
        }

        /// <summary>A computed drag-drop target: where the line is drawn, and how to perform the move.</summary>
        public sealed record DragDropPlan(bool Valid, double IndicatorY, double IndicatorX, Guid? ParentId, Track? BeforeTrack);

        /// <summary>
        /// Works out where a dragged track would land for a pointer at content-Y <paramref name="contentY"/>:
        /// the insertion line position/indent, the resulting parent group, and the track to insert before.
        /// </summary>
        public DragDropPlan ComputeDrop(double contentY, Track dragged)
        {
            var rows = new List<(TrackLaneViewModel Lane, double Top)>();
            for (var i = 0; i < Lanes.Count; i++)
            {
                if (Lanes[i] is TrackLaneViewModel t) rows.Add((t, RowTop(i)));
            }

            if (rows.Count == 0) return new DragDropPlan(false, 0, 0, null, null);

            var hoverIdx = -1;
            for (var i = 0; i < rows.Count; i++)
            {
                if (contentY < rows[i].Top + TrackLaneViewModel.RowHeight) { hoverIdx = i; break; }
            }

            Guid? parentId;
            Track? before;
            double indicatorY;
            int depth;

            if (hoverIdx < 0)
            {
                // Below all rows → append at the bottom, top level.
                parentId = null;
                before = null;
                indicatorY = RowsTotalHeight;
                depth = 0;
            }
            else
            {
                var hover = rows[hoverIdx].Lane;
                var top = rows[hoverIdx].Top;
                var topHalf = contentY < top + TrackLaneViewModel.RowHeight / 2;

                if (topHalf && !hover.IsMaster)
                {
                    // Insert just above the hovered row, as its sibling.
                    parentId = hover.Model.ParentId;
                    before = hover.Model;
                    indicatorY = top;
                    depth = hover.IndentLevel;
                }
                else if (!topHalf && hover.IsGroup && !hover.Model.GroupCollapsed)
                {
                    // Drop into the group as its first child.
                    parentId = hover.Model.Id;
                    before = hoverIdx + 1 < rows.Count && rows[hoverIdx + 1].Lane.IndentLevel == hover.IndentLevel + 1
                        ? rows[hoverIdx + 1].Lane.Model
                        : null;
                    indicatorY = top + TrackLaneViewModel.RowHeight;
                    depth = hover.IndentLevel + 1;
                }
                else
                {
                    // Insert after the hovered row's whole subtree, as its sibling (or top level if master).
                    parentId = hover.IsMaster ? null : hover.Model.ParentId;
                    var baseDepth = hover.IsMaster ? 0 : hover.IndentLevel;
                    Track? next = null;
                    var nextTop = RowsTotalHeight;
                    for (var j = hoverIdx + 1; j < rows.Count; j++)
                    {
                        if (rows[j].Lane.IndentLevel <= baseDepth) { next = rows[j].Lane.Model; nextTop = rows[j].Top; break; }
                    }

                    before = next;
                    indicatorY = nextTop;
                    depth = baseDepth;
                }
            }

            var valid = IsDropValid(dragged, parentId, before);
            return new DragDropPlan(valid, indicatorY, depth * 8.0, parentId, before);
        }

        // A drop is invalid if it would put a group inside its own subtree, or is a no-op self-drop.
        private bool IsDropValid(Track dragged, Guid? parentId, Track? before)
        {
            if (dragged.Kind == TrackKind.Master) return false;
            if (before is not null && (ReferenceEquals(before, dragged) ||
                                       IsDescendantOf(before, dragged.Id, _project.Current.Tracks))) return false;
            if (parentId is { } pid && (pid == dragged.Id ||
                                        (_project.Current.Tracks.FirstOrDefault(x => x.Id == pid) is { } p
                                         && IsDescendantOf(p, dragged.Id, _project.Current.Tracks)))) return false;
            return true;
        }

        /// <summary>Performs a drag-drop move computed by <see cref="ComputeDrop"/>.</summary>
        public void MoveTrack(Track dragged, DragDropPlan plan)
        {
            if (!plan.Valid) return;
            _history.Capture("Move track");

            var tracks = _project.Current.Tracks;
            var subtree = SubtreeOf(dragged, tracks);
            var before = plan.BeforeTrack;
            foreach (var t in subtree) tracks.Remove(t);

            dragged.ParentId = plan.ParentId;

            int insertAt;
            if (before is not null && tracks.Contains(before))
            {
                insertAt = tracks.IndexOf(before);
            }
            else if (plan.ParentId is { } pid)
            {
                var parent = tracks.FirstOrDefault(x => x.Id == pid);
                insertAt = parent is not null ? tracks.IndexOf(parent) + 1 : tracks.Count;
            }
            else
            {
                var master = _project.Current.Master;
                insertAt = master is not null ? tracks.IndexOf(master) + 1 : 0;
            }

            insertAt = Math.Clamp(insertAt, 0, tracks.Count);
            tracks.InsertRange(insertAt, subtree);

            _events.Publish(new TracksChangedEvent());
            Rebuild();
            _selection.SelectTrack(dragged);
        }

        private bool HasSelectedAncestor(Track track, HashSet<Guid> selected)
        {
            var pid = track.ParentId;
            var guard = 0;
            while (pid is { } id && guard++ < 256)
            {
                if (selected.Contains(id)) return true;
                pid = _project.Current.Tracks.FirstOrDefault(x => x.Id == id)?.ParentId;
            }

            return false;
        }

        // A track followed by all its descendants, in document order.
        private static List<Track> SubtreeOf(Track root, List<Track> tracks)
        {
            var result = new List<Track> { root };
            foreach (var t in tracks)
            {
                if (!ReferenceEquals(t, root) && IsDescendantOf(t, root.Id, tracks)) result.Add(t);
            }

            return result;
        }

        private static bool IsDescendantOf(Track track, Guid ancestorId, List<Track> tracks)
        {
            var pid = track.ParentId;
            var guard = 0;
            while (pid is { } id && guard++ < 256)
            {
                if (id == ancestorId) return true;
                pid = tracks.FirstOrDefault(x => x.Id == id)?.ParentId;
            }

            return false;
        }

        public void RemoveAutomationLane(AutomationLaneViewModel lane)
        {
            _history.Capture("Remove automation");
            var track = lane.OwnerTrack;
            track.AutoLanes.Remove(lane.Lane);
            track.CommitAutoLanes();
            RebuildRows();
            _events.Publish(new TracksChangedEvent());
        }

        public void AddInstrumentTrack() => CreateInstrumentTrack(InstrumentRegistry.DefaultInstrumentId, _trackLanes.Count);

        public void AddAudioTrack()
        {
            _history.Capture("Add audio track");
            var track = new Track { Name = $"Audio {AudioTrackNumber()}", Kind = TrackKind.Audio, ColorKey = "CatppuccinTeal" };
            InsertTrack(track, _trackLanes.Count);
        }

        public void NotifyTrackChanged(Track track) => _events.Publish(new TrackChangedEvent(track));

        /// <summary>Creates a new instrument track with the given instrument type at the given lane index.</summary>
        public void CreateInstrumentTrack(string instrumentId, int laneIndex)
        {
            IInstrument instrument;
            try { instrument = _instruments.Create(instrumentId); }
            catch { return; }
            CreateInstrumentTrack(instrument, laneIndex);
        }

        /// <summary>Creates an instrument track hosting an already-built instrument (e.g. a loaded preset or
        /// sound-font sampler dragged from the library).</summary>
        public void CreateInstrumentTrack(IInstrument instrument, int laneIndex)
        {
            _history.Capture("Add instrument track");

            var track = new Track
            {
                Name = $"{instrument.Name} {InstrumentTrackNumber()}",
                Kind = TrackKind.Instrument,
                ColorKey = "CatppuccinMauve"
            };
            track.Instruments.Add(new Core.Models.Audio.InstrumentSlot(instrument));
            track.CommitInstruments();
            InsertTrack(track, laneIndex);
        }

        /// <summary>Creates an instrument track with a Sampler holding the dropped sound font.</summary>
        public void CreateSoundFontTrack(string path, int laneIndex)
        {
            if (!string.IsNullOrEmpty(path)) _ = CreateSoundFontTrackAsync(path, laneIndex);
        }

        /// <summary>Creates an instrument track from a dropped instrument preset (.ongenpreset).</summary>
        public void CreateInstrumentPresetTrack(string presetPath, int laneIndex)
        {
            if (LoadPresetInstrument(presetPath) is { } instrument) CreateInstrumentTrack(instrument, laneIndex);
        }

        // Loads the sound font off the UI thread *first*, then creates the track — so the inspector card,
        // built when the new track is selected, reads the sampler's already-applied patch (name + regions)
        // instead of showing "(no instrument loaded)".
        private async Task CreateSoundFontTrackAsync(string path, int laneIndex)
        {
            var sampler = new SamplerInstrument();
            var loader = App.ServiceProvider?.GetService<ISamplerLoadService>();
            if (loader is not null)
            {
                var result = await Task.Run(() => loader.Load(path));
                if (result is not null) sampler.ApplyLoad(result);
            }
            CreateInstrumentTrack(sampler, laneIndex);
        }

        private IInstrument? LoadPresetInstrument(string path)
        {
            try
            {
                var effects = App.ServiceProvider?.GetService<IEffectRegistry>();
                if (effects is null) return null;
                using var fs = File.OpenRead(path);
                return Ongenet.Core.Persistence.PresetFile.Load(fs, _instruments, effects)?.Instrument;
            }
            catch { return null; }
        }

        // Inserts a track into the project + lanes at the given track index, publishes, and selects it.
        private void InsertTrack(Track track, int index)
        {
            // The master is pinned at row 0; never insert another track above it.
            var minIndex = _project.Current.Master is not null ? 1 : 0;
            index = Math.Clamp(index, minIndex, _trackLanes.Count);
            _project.Current.Tracks.Insert(index, track);
            _trackLanes.Insert(index, new TrackLaneViewModel(track, Metrics, this, this));
            RecomputeIndents();
            RebuildRows();
            OnPropertyChanged(nameof(LaneCount));
            ResizeArrangement();
            _events.Publish(new TracksChangedEvent());
            _selection.SelectTrack(track);
        }

        private static Clip CloneClip(Clip src)
        {
            var copy = new Clip
            {
                Name = src.Name,
                StartBeat = src.StartBeat,
                LengthBeats = src.LengthBeats,
                IsAudio = src.IsAudio,
                StretchToTempo = src.StretchToTempo,
                SourceTempo = src.SourceTempo,
                AudioFilePath = src.AudioFilePath,
                Waveform = src.Waveform,
                Samples = src.Samples,
                SourceOffsetSeconds = src.SourceOffsetSeconds,
                SourceLengthSeconds = src.SourceLengthSeconds
            };
            foreach (var note in src.Notes)
            {
                copy.Notes.Add(new MidiNote
                {
                    Note = note.Note,
                    StartBeat = note.StartBeat,
                    LengthBeats = note.LengthBeats,
                    Velocity = note.Velocity
                });
            }

            return copy;
        }

        private int InstrumentTrackNumber()
            => _trackLanes.Count(l => l.Model.Kind == TrackKind.Instrument) + 1;

        private void RefreshTrack(Core.Models.Audio.Track track)
        {
            var lane = _trackLanes.FirstOrDefault(l => ReferenceEquals(l.Model, track));
            lane?.RefreshFromModel();

            // A colour change must repaint the gutter rails, which are a snapshot list rebuilt only on a
            // full Rebuild(). The changed track's colour also forms an ancestor rail on its descendants,
            // so refresh every lane's bars (cheap for realistic track counts).
            foreach (var l in _trackLanes) l.GutterBars = BuildGutterBars(l.Model);
        }

        private void RefreshClip(Core.Models.Audio.Clip clip)
        {
            foreach (var lane in _trackLanes)
            {
                var clipVm = lane.Clips.FirstOrDefault(c => ReferenceEquals(c.Model, clip));
                if (clipVm is null) continue;
                clipVm.RefreshFromModel();
                return;
            }
        }

        // A clip was added to a track's model elsewhere (e.g. recording): create its lane view model.
        private void OnClipAdded(Core.Models.Audio.Track track, Core.Models.Audio.Clip clip)
        {
            var lane = _trackLanes.FirstOrDefault(l => ReferenceEquals(l.Model, track));
            if (lane is null) return;
            if (lane.Clips.Any(c => ReferenceEquals(c.Model, clip))) return;
            lane.Clips.Add(new ClipViewModel(clip, lane.Model, Metrics, this));
            ExtendTimeline(clip.EndBeat);
        }

        private void RefreshClipNotes(Core.Models.Audio.Clip clip)
        {
            foreach (var lane in _trackLanes)
            {
                var clipVm = lane.Clips.FirstOrDefault(c => ReferenceEquals(c.Model, clip));
                if (clipVm is null) continue;
                clipVm.NotifyNotesChanged();
                return;
            }
        }

        private void OnSelectionChanged()
        {
            // Mirror the service's selection into the lane/clip view state without echoing
            // back into the service.
            _syncingSelection = true;
            try
            {
                var selectedTrack = _selection.SelectedTrack;
                var selectedClip = _selection.SelectedClip;
                var selectedTracks = _selection.SelectedTracks;

                SelectedLane = _trackLanes.FirstOrDefault(l => ReferenceEquals(l.Model, selectedTrack));

                foreach (var lane in _trackLanes)
                {
                    // Highlight every multi-selected track (so the user sees the grouping selection).
                    lane.IsSelected = selectedTracks.Contains(lane.Model);
                    foreach (var clip in lane.Clips)
                    {
                        clip.IsSelected = ReferenceEquals(clip.Model, selectedClip);
                    }
                }
            }
            finally
            {
                _syncingSelection = false;
            }
        }
    }
}
