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
        private const double DefaultNoteLengthBeats = 1.0;

        private readonly ISelectionService _selection;
        private readonly IEventAggregator _events;
        private readonly IEditModeService _editMode;
        private readonly IPreviewService _preview;

        private Clip? _clip;
        private Track? _track;

        public PianoRollViewModel(ISelectionService selection, IEventAggregator events,
            IEditModeService editMode, IPreviewService preview)
        {
            _selection = selection;
            _events = events;
            _editMode = editMode;
            _preview = preview;

            Keys = BuildKeys();

            _selection.SelectionChanged += OnSelectionChanged;
            _events.Subscribe<ClipChangedEvent>(e => OnClipGeometryChanged(e.Clip));
            _editMode.ModeChanged += () => OnPropertyChanged(nameof(IsSelectMode));
            _preview.ActiveNotesChanged += UpdateKeyHighlights;

            Bind(_selection.SelectedClip, _selection.SelectedTrack);
        }

        private void UpdateKeyHighlights()
        {
            foreach (var key in Keys) key.IsActive = _preview.IsActive(key.MidiNote);
        }

        /// <summary>True when Select (rubber-band multi-select) mode is active.</summary>
        public bool IsSelectMode => _editMode.Mode == EditMode.Select;

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

            var model = new MidiNote
            {
                Note = note,
                StartBeat = Metrics.Snap(beat),
                LengthBeats = DefaultNoteLengthBeats
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
            Publish();
        }

        /// <summary>Deletes a note.</summary>
        public void DeleteNote(NoteViewModel note)
        {
            if (_clip is null) return;
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
            foreach (var note in selected)
            {
                _clip.Notes.Remove(note.Model);
                Notes.Remove(note);
            }

            Publish();
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
