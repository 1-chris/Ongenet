using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels
{
    /// <summary>
    /// Bottom-centre inspector for the selected clip: name, start and length (in beats).
    /// Edits mutate the underlying <see cref="Clip"/> and publish a <see cref="ClipChangedEvent"/>
    /// so the timeline re-lays-out the clip.
    /// </summary>
    public class ClipInspectorViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly IEventAggregator _events;
        private readonly Services.IHistoryService _history;

        public ClipInspectorViewModel(ISelectionService selection, IEventAggregator events,
            Services.IHistoryService history)
        {
            _selection = selection;
            _events = events;
            _history = history;
            _selection.SelectionChanged += OnSelectionChanged;
        }

        private Clip? Clip => _selection.SelectedClip;

        /// <summary>
        /// Whether the clip inspector should occupy the bottom panel. It is the fallback: hidden
        /// when a MIDI clip is selected (piano roll shows) or when an instrument track with no clip
        /// is selected (instrument inspector shows).
        /// </summary>
        public bool IsVisible
        {
            get
            {
                var clip = _selection.SelectedClip;
                if (clip is { IsMidi: true }) return false; // piano roll owns the panel
                var track = _selection.SelectedTrack;
                if (clip is null && track is { Kind: TrackKind.Instrument }) return false;
                return true;
            }
        }

        /// <summary>Whether a clip is selected (controls visible vs empty-state).</summary>
        public bool HasClip => Clip is not null;

        /// <summary>Name of the track that owns the selected clip, for context.</summary>
        public string TrackName => _selection.SelectedTrack?.Name ?? string.Empty;

        public string Name
        {
            get => Clip?.Name ?? string.Empty;
            set
            {
                if (Clip is null || Clip.Name == value) return;
                _history.Capture("Rename clip");
                Clip.Name = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public double StartBeat
        {
            get => Clip?.StartBeat ?? 0.0;
            set
            {
                if (Clip is null || value < 0 || Clip.StartBeat == value) return;
                _history.Capture("Move clip");
                Clip.StartBeat = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public double LengthBeats
        {
            get => Clip?.LengthBeats ?? 0.0;
            set
            {
                if (Clip is null || value <= 0 || Clip.LengthBeats == value) return;
                _history.Capture("Resize clip");
                Clip.LengthBeats = value;
                OnPropertyChanged();
                Notify();
            }
        }

        private void Notify()
        {
            if (Clip is not null) _events.Publish(new ClipChangedEvent(Clip));
        }

        private void OnSelectionChanged()
        {
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(HasClip));
            OnPropertyChanged(nameof(TrackName));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(StartBeat));
            OnPropertyChanged(nameof(LengthBeats));
        }
    }
}
