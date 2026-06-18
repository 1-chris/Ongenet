using System.Collections.Generic;
using System.ComponentModel;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Desktop.ViewModels.Timeline
{
    /// <summary>
    /// View model for a single <see cref="Clip"/> on a lane. Exposes its pixel position and
    /// width by routing the clip's beat values through the shared <see cref="TimelineMetrics"/>,
    /// and re-raises them when zoom changes so the lane re-lays-out.
    /// </summary>
    public class ClipViewModel : ViewModelBase
    {
        private readonly TimelineMetrics _metrics;
        private bool _isSelected;

        public ClipViewModel(Clip model, Track owner, TimelineMetrics metrics, IClipActions actions)
        {
            Model = model;
            Owner = owner;
            _metrics = metrics;
            _metrics.PropertyChanged += OnMetricsChanged;
            DuplicateCommand = new RelayCommand(() => actions.DuplicateClip(this));
            DeleteCommand = new RelayCommand(() => actions.DeleteClip(this));
        }

        public RelayCommand DuplicateCommand { get; }
        public RelayCommand DeleteCommand { get; }

        /// <summary>The underlying domain clip.</summary>
        public Clip Model { get; }

        /// <summary>The track that owns this clip (needed when reporting selection).</summary>
        public Track Owner { get; }

        public string Name => Model.Name;

        /// <summary>Left edge of the clip on the lane canvas, in pixels.</summary>
        public double Left => _metrics.BeatsToPixels(Model.StartBeat);

        /// <summary>Width of the clip, in pixels.</summary>
        public double Width => _metrics.BeatsToPixels(Model.LengthBeats);

        /// <summary>Waveform peaks for an audio clip, or null.</summary>
        public AudioWaveform? Waveform => Model.Waveform;

        /// <summary>True when this clip renders a waveform.</summary>
        public bool IsAudio => Model.IsAudio;

        /// <summary>True when this clip renders MIDI notes.</summary>
        public bool IsMidi => Model.IsMidi;

        /// <summary>The clip's notes, for the miniature view.</summary>
        public IReadOnlyList<MidiNote> Notes => Model.Notes;

        /// <summary>Clip length in beats, for mapping notes into the miniature view.</summary>
        public double ClipLengthBeats => Model.LengthBeats;

        private int _notesRevision;

        /// <summary>Bumped whenever the clip's notes change, to force the miniature view to repaint.</summary>
        public int NotesRevision
        {
            get => _notesRevision;
            private set => SetField(ref _notesRevision, value);
        }

        /// <summary>Signals that the clip's notes changed (repaints the miniature view).</summary>
        public void NotifyNotesChanged() => NotesRevision++;

        private int _waveformRevision;

        /// <summary>Bumped whenever the clip's waveform grows in place (live recording), to repaint it.</summary>
        public int WaveformRevision
        {
            get => _waveformRevision;
            private set => SetField(ref _waveformRevision, value);
        }

        /// <summary>Whether this clip is the current selection.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        /// <summary>Re-reads name/start/length/waveform after the model changes (edit or decode).</summary>
        public void RefreshFromModel()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Waveform));
            OnPropertyChanged(nameof(IsAudio));
            OnPropertyChanged(nameof(IsMidi));
            OnPropertyChanged(nameof(ClipLengthBeats));
            // Repaint the miniature note view and waveform too (a resize/grow changes their mapping).
            NotifyNotesChanged();
            WaveformRevision++;
        }

        private void OnMetricsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimelineMetrics.PixelsPerBeat))
            {
                OnPropertyChanged(nameof(Left));
                OnPropertyChanged(nameof(Width));
            }
        }
    }
}
