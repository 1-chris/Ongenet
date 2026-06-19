using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.ViewModels.PianoRoll;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// The piano-roll editor shown in the bottom panel when a MIDI clip is selected. Edits the
    /// selected clip's notes (add/move/resize/delete), previews them through the owning track's
    /// instrument, and publishes <see cref="ClipNotesChangedEvent"/> so the arrange-view mini
    /// view updates live.
    /// </summary>
    public class PianoRollViewModel : ViewModelBase
    {
        // The pen length: new notes are painted at the length of the last note clicked/resized.
        private double _lastNoteLength = 1.0;

        // Snapshot of the selection at the start of a multi-note move/resize drag.
        private readonly List<(NoteViewModel note, double start, double length, int pitch)> _selectionBaseline = new();

        private readonly IProjectService _project;
        private readonly ISelectionService _selection;
        private readonly IEventAggregator _events;
        private readonly IEditModeService _editMode;
        private readonly IPreviewService _preview;
        private readonly Services.IHistoryService _history;

        private Clip? _clip;
        private Track? _track;

        public PianoRollViewModel(IProjectService project, ISelectionService selection,
            IEventAggregator events, IEditModeService editMode, IPreviewService preview,
            Services.IHistoryService history)
        {
            _project = project;
            _selection = selection;
            _events = events;
            _editMode = editMode;
            _preview = preview;
            _history = history;

            Keys = BuildKeys();

            _selection.SelectionChanged += OnSelectionChanged;
            _events.Subscribe<ClipChangedEvent>(e => OnClipGeometryChanged(e.Clip));
            // Keep the grid's bar lines in step with the project time signature (changed via the
            // transport bar), exactly as the arrange view does.
            _events.Subscribe<ArrangementLengthChangedEvent>(_ => SyncTimeSignature());
            _project.ProjectChanged += SyncTimeSignature;
            _editMode.ModeChanged += RaiseModeProperties;
            _preview.ActiveNotesChanged += UpdateKeyHighlights;

            SyncTimeSignature();
            Bind(_selection.SelectedClip, _selection.SelectedTrack);
        }

        /// <summary>Pulls beats-per-bar from the project time signature into the editor metrics.</summary>
        private void SyncTimeSignature()
            => Metrics.BeatsPerBar = Math.Max(1, _project.Current.TimeSignature.Numerator);

        private void UpdateKeyHighlights()
        {
            foreach (var key in Keys) key.IsActive = _preview.IsActive(key.MidiNote);
        }

        // The three tool toggles act like radio buttons: exactly one is always active. Setting one
        // true switches the shared mode; "unchecking" the active one is refused by re-raising the
        // property so its toggle snaps back on.
        /// <summary>True when the default Edit tool is active (move/resize/draw single or multiple notes).</summary>
        public bool IsEditMode
        {
            get => _editMode.Mode == EditMode.Edit;
            set { if (value) _editMode.Mode = EditMode.Edit; else OnPropertyChanged(); }
        }

        /// <summary>True when Select (rubber-band multi-select) mode is active.</summary>
        public bool IsSelectMode
        {
            get => _editMode.Mode == EditMode.Select;
            set { if (value) _editMode.Mode = EditMode.Select; else OnPropertyChanged(); }
        }

        /// <summary>True when the Slice tool is active (click-drag a line to cut notes).</summary>
        public bool IsSliceMode
        {
            get => _editMode.Mode == EditMode.Slice;
            set { if (value) _editMode.Mode = EditMode.Slice; else OnPropertyChanged(); }
        }

        private void RaiseModeProperties()
        {
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(IsSelectMode));
            OnPropertyChanged(nameof(IsSliceMode));
        }

        /// <summary>Raised when a new clip is bound, so the view can fit it to the available width.</summary>
        public event Action? ClipBound;

        /// <summary>Piano keys / rows (A0..C8), top to bottom.</summary>
        public IReadOnlyList<PianoKeyViewModel> Keys { get; }

        /// <summary>The notes currently shown.</summary>
        public ObservableCollection<NoteViewModel> Notes { get; } = new();

        /// <summary>Shared time/pitch↔pixel mapping.</summary>
        public PianoRollMetrics Metrics { get; } = new();

        /// <summary>True when a MIDI clip is selected (drives bottom-panel visibility).</summary>
        public bool IsVisible => _clip is not null;

        /// <summary>Name of the clip being edited, for the header.</summary>
        public string ClipName => _clip?.Name ?? string.Empty;

        // --- Editing ---

        /// <summary>Adds a note at the given clip-local beat and pitch; returns its view model.</summary>
        public NoteViewModel? AddNote(double beat, int note)
        {
            if (_clip is null) return null;
            _history.Capture("Add note");

            var model = new MidiNote
            {
                Note = note,
                StartBeat = Metrics.Snap(beat),
                LengthBeats = _lastNoteLength
            };
            _clip.Notes.Add(model);

            var vm = new NoteViewModel(model, Metrics);
            Notes.Add(vm);
            SelectOnly(vm);
            Publish();
            return vm;
        }

        /// <summary>Moves a note to a new clip-local beat + pitch.</summary>
        public void MoveNote(NoteViewModel note, double beat, int pitch)
        {
            if (_clip is null) return;
            note.Model.StartBeat = Math.Max(0, Metrics.Snap(beat));
            note.Model.Note = pitch;
            note.RefreshFromModel();
            Publish();
        }

        /// <summary>Resizes a note to a new length (snapped, clamped).</summary>
        public void ResizeNote(NoteViewModel note, double lengthBeats)
        {
            if (_clip is null) return;
            var snapped = Metrics.Snap(lengthBeats);
            note.Model.LengthBeats = Math.Max(Metrics.SnapBeats, snapped);
            note.RefreshFromModel();
            _lastNoteLength = note.Model.LengthBeats; // remember as the pen length
            Publish();
        }

        /// <summary>Records a note length as the pen length for future drawn notes.</summary>
        public void RememberLength(double beats)
        {
            if (beats > 0) _lastNoteLength = beats;
        }

        /// <summary>Deletes a note.</summary>
        public void DeleteNote(NoteViewModel note)
        {
            if (_clip is null) return;
            _history.Capture("Delete note");
            _clip.Notes.Remove(note.Model);
            Notes.Remove(note);
            Publish();
        }

        public void SelectOnly(NoteViewModel note)
        {
            foreach (var n in Notes) n.IsSelected = ReferenceEquals(n, note);
        }

        /// <summary>Selects every note intersecting the rectangle (grid pixel coordinates).</summary>
        public void SelectNotesInRect(double x0, double y0, double x1, double y1)
        {
            var minX = Math.Min(x0, x1);
            var maxX = Math.Max(x0, x1);
            var minY = Math.Min(y0, y1);
            var maxY = Math.Max(y0, y1);

            foreach (var note in Notes)
            {
                note.IsSelected = note.Left < maxX && note.Left + note.Width > minX
                                  && note.Top < maxY && note.Top + note.Height > minY;
            }
        }

        /// <summary>Deletes all notes currently marked selected.</summary>
        public void DeleteSelectedNotes()
        {
            if (_clip is null) return;
            var selected = Notes.Where(n => n.IsSelected).ToList();
            if (selected.Count == 0) return;
            _history.Capture("Delete notes");
            foreach (var note in selected)
            {
                _clip.Notes.Remove(note.Model);
                Notes.Remove(note);
            }

            Publish();
        }

        /// <summary>The notes currently marked selected.</summary>
        public IReadOnlyList<NoteViewModel> SelectedNotes => Notes.Where(n => n.IsSelected).ToList();

        /// <summary>How many notes are currently selected.</summary>
        public int SelectedCount => Notes.Count(n => n.IsSelected);

        // --- Multi-note edit (Edit tool, more than one note selected) ---

        /// <summary>Snapshots the selection's positions/lengths at the start of a drag.</summary>
        public void CaptureSelectionBaseline()
        {
            _selectionBaseline.Clear();
            foreach (var n in Notes)
            {
                if (n.IsSelected)
                    _selectionBaseline.Add((n, n.Model.StartBeat, n.Model.LengthBeats, n.Model.Note));
            }
        }

        /// <summary>Moves every selected note by a (grid-snapped) beat delta and a pitch delta, from baseline.</summary>
        public void MoveSelectionBy(double deltaBeats, int deltaPitch)
        {
            if (_clip is null) return;
            var step = Metrics.SnapBeats;
            var snappedDelta = step > 0 ? Math.Round(deltaBeats / step) * step : deltaBeats;

            foreach (var (note, start, _, pitch) in _selectionBaseline)
            {
                note.Model.StartBeat = Math.Max(0, start + snappedDelta);
                note.Model.Note = Math.Clamp(pitch + deltaPitch, 0, 127);
                note.RefreshFromModel();
            }

            Publish();
        }

        /// <summary>Scales every selected note's length by a factor (relative to its own size), from baseline.</summary>
        public void ScaleSelectionLength(double factor)
        {
            if (_clip is null || factor <= 0) return;
            foreach (var (note, _, length, _) in _selectionBaseline)
            {
                var snapped = Metrics.Snap(length * factor);
                note.Model.LengthBeats = Math.Max(Metrics.SnapBeats, snapped);
                note.RefreshFromModel();
            }

            Publish();
        }

        // --- Slicing ---

        /// <summary>
        /// Splits a note at the given clip-local beat into two abutting notes (same pitch/velocity).
        /// No-op if the cut falls outside the note. History is captured by the caller per gesture.
        /// </summary>
        public void SliceNote(NoteViewModel note, double cutBeatInClip)
        {
            if (_clip is null) return;
            var start = note.Model.StartBeat;
            var end = note.Model.EndBeat;
            if (cutBeatInClip <= start + 1e-6 || cutBeatInClip >= end - 1e-6) return;

            note.Model.LengthBeats = cutBeatInClip - start;
            note.RefreshFromModel();

            var right = new MidiNote
            {
                Note = note.Model.Note,
                StartBeat = cutBeatInClip,
                LengthBeats = end - cutBeatInClip,
                Velocity = note.Model.Velocity
            };
            _clip.Notes.Add(right);
            Notes.Add(new NoteViewModel(right, Metrics));
            Publish();
        }

        // --- Bulk note operations (import / generator / arpeggiator) ---

        /// <summary>True when a MIDI clip is bound (for toolbar command enabling).</summary>
        public bool HasClip => _clip is not null;

        /// <summary>The bound clip's notes (empty when none) — for export.</summary>
        public IReadOnlyList<MidiNote> CurrentNotes => _clip?.Notes ?? (IReadOnlyList<MidiNote>)Array.Empty<MidiNote>();

        /// <summary>The bound clip's length in beats (0 when none) — for export.</summary>
        public double CurrentLengthBeats => _clip?.LengthBeats ?? 0;

        /// <summary>The project tempo in BPM — for export.</summary>
        public double ProjectTempo => _project.Current.Tempo.BeatsPerMinute;

        /// <summary>The project time signature — for export.</summary>
        public TimeSignature ProjectTimeSignature => _project.Current.TimeSignature;

        /// <summary>Replaces all of the bound clip's notes (e.g. MIDI import or "replace" generate).</summary>
        public void ReplaceNotes(IReadOnlyList<MidiNote> notes, double lengthBeats, string label)
        {
            if (_clip is null) return;
            _history.Capture(label);
            _clip.Notes.Clear();
            _clip.Notes.AddRange(notes);
            GrowClipToFit(lengthBeats);
            RebuildNotes();
            Publish();
        }

        /// <summary>Adds notes to the bound clip without removing existing ones (e.g. "insert" generate).</summary>
        public void InsertNotes(IReadOnlyList<MidiNote> notes, string label)
        {
            if (_clip is null || notes.Count == 0) return;
            _history.Capture(label);
            var maxEnd = _clip.LengthBeats;
            foreach (var n in notes)
            {
                _clip.Notes.Add(n);
                if (n.EndBeat > maxEnd) maxEnd = n.EndBeat;
            }

            GrowClipToFit(maxEnd);
            RebuildNotes();
            Publish();
        }

        /// <summary>Replaces the currently-selected notes with a new set (used by "Convert to arpeggio").</summary>
        public void ReplaceSelectionWith(IReadOnlyList<MidiNote> notes, string label)
        {
            if (_clip is null) return;
            var selected = Notes.Where(n => n.IsSelected).Select(n => n.Model).ToHashSet();
            if (selected.Count == 0) return;

            _history.Capture(label);
            _clip.Notes.RemoveAll(selected.Contains);
            var maxEnd = _clip.LengthBeats;
            foreach (var n in notes)
            {
                _clip.Notes.Add(n);
                if (n.EndBeat > maxEnd) maxEnd = n.EndBeat;
            }

            GrowClipToFit(maxEnd);
            RebuildNotes();
            Publish();
        }

        // Grows the clip (and grid) so it spans at least the given beat length, rounded up to a bar.
        // Never shrinks, so it won't surprise the arrange view by cutting material.
        private void GrowClipToFit(double beats)
        {
            if (_clip is null) return;
            var bar = Math.Max(1, Metrics.BeatsPerBar);
            var bars = Math.Max(1, (int)Math.Ceiling(beats / bar - 1e-6));
            var target = bars * bar;
            if (target > _clip.LengthBeats)
            {
                _clip.LengthBeats = target;
                // Tell the arrange view so the clip rectangle grows to match the new material.
                _events.Publish(new ClipChangedEvent(_clip));
            }

            Metrics.TotalBeats = _clip.LengthBeats;
        }

        // Rebuilds the note view models from the bound clip's note list.
        private void RebuildNotes()
        {
            Notes.Clear();
            if (_clip is null) return;
            foreach (var n in _clip.Notes) Notes.Add(new NoteViewModel(n, Metrics));
        }

        /// <summary>Previews a pitch through the selected instrument (and highlights the key).</summary>
        public void PreviewOn(int note) => _preview.NoteOn(note);

        /// <summary>Stops a previewed pitch.</summary>
        public void PreviewOff(int note) => _preview.NoteOff(note);

        /// <summary>Fits the visible clip to the given pixel width (called by the view).</summary>
        public void FitToWidth(double width)
        {
            if (_clip is null || Metrics.TotalBeats <= 0 || width <= 0) return;
            Metrics.PixelsPerBeat = width / Metrics.TotalBeats;
        }

        private void Publish()
        {
            if (_clip is not null) _events.Publish(new ClipNotesChangedEvent(_clip));
        }

        private void OnSelectionChanged()
        {
            var clip = _selection.SelectedClip;
            Bind(clip is { IsMidi: true } ? clip : null, _selection.SelectedTrack);
        }

        private void Bind(Clip? clip, Track? track)
        {
            if (clip is null || !clip.IsMidi)
            {
                _clip = null;
                _track = null;
                Notes.Clear();
                OnPropertyChanged(nameof(IsVisible));
                OnPropertyChanged(nameof(ClipName));
                return;
            }

            _clip = clip;
            _track = track;
            Metrics.TotalBeats = clip.LengthBeats;

            Notes.Clear();
            foreach (var note in clip.Notes)
            {
                Notes.Add(new NoteViewModel(note, Metrics));
            }

            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(ClipName));
            ClipBound?.Invoke();
        }

        private void OnClipGeometryChanged(Clip clip)
        {
            // The bound clip was resized in the arrange view: grow/shrink the grid to match.
            if (!ReferenceEquals(clip, _clip)) return;
            Metrics.TotalBeats = clip.LengthBeats;
        }

        private static IReadOnlyList<PianoKeyViewModel> BuildKeys()
        {
            var metrics = new PianoRollMetrics();
            var keys = new List<PianoKeyViewModel>();
            for (var note = PianoRollMetrics.HighNote; note >= PianoRollMetrics.LowNote; note--)
            {
                keys.Add(new PianoKeyViewModel(note, metrics));
            }

            return keys;
        }
    }
}
