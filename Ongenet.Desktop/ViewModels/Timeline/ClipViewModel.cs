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
            ReverseCommand = new RelayCommand(() => actions.ReverseClip(this));
        }

        public RelayCommand DuplicateCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand ReverseCommand { get; }

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

        /// <summary>
        /// Fraction (0..1) of the source where this clip's waveform window begins — non-zero only for
        /// the right-hand piece of a sliced clip, so it shows just its portion of the source.
        /// </summary>
        public double WaveStartFraction
        {
            get
            {
                if (Model.Waveform is not { } wf) return 0.0;
                var dur = wf.DurationSeconds;
                return dur > 0 ? System.Math.Clamp(Model.SourceOffsetSeconds / dur, 0.0, 1.0) : 0.0;
            }
        }

        /// <summary>Fraction (0..1) of the source where this clip's waveform window ends (1 = whole source).</summary>
        public double WaveEndFraction
        {
            get
            {
                if (Model.Waveform is not { } wf) return 1.0;
                var dur = wf.DurationSeconds;
                if (dur <= 0) return 1.0;
                var end = Model.SourceLengthSeconds is { } len ? Model.SourceOffsetSeconds + len : dur;
                return System.Math.Clamp(end / dur, 0.0, 1.0);
            }
        }

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

        private double _fadeInBeats;
        private double _fadeOutBeats;
        private int _fadeRevision;
        private AudioWaveform? _fadeInWaveform;
        private AudioWaveform? _fadeOutWaveform;

        /// <summary>Crossfade-in length in beats (set by the timeline from clip overlaps). 0 = no fade-in.</summary>
        public double FadeInBeats => _fadeInBeats;

        /// <summary>Crossfade-out length in beats (set by the timeline from clip overlaps). 0 = no fade-out.</summary>
        public double FadeOutBeats => _fadeOutBeats;

        /// <summary>Crossfade-in width in pixels, for the fade visual.</summary>
        public double FadeInWidth => _metrics.BeatsToPixels(_fadeInBeats);

        /// <summary>Crossfade-out width in pixels, for the fade visual.</summary>
        public double FadeOutWidth => _metrics.BeatsToPixels(_fadeOutBeats);

        /// <summary>Crossfaded waveform for the fade-in (left) overlap region, or null.</summary>
        public AudioWaveform? FadeInWaveform => _fadeInWaveform;

        /// <summary>Crossfaded waveform for the fade-out (right) overlap region, or null.</summary>
        public AudioWaveform? FadeOutWaveform => _fadeOutWaveform;

        /// <summary>Bumped whenever the fade lengths or crossfaded waveforms change, to repaint the fade visual.</summary>
        public int FadeRevision => _fadeRevision;

        /// <summary>Sets the crossfaded overlap waveforms (left/right) and repaints the fade visual.</summary>
        public void SetFadeWaveforms(AudioWaveform? fadeIn, AudioWaveform? fadeOut)
        {
            if (ReferenceEquals(fadeIn, _fadeInWaveform) && ReferenceEquals(fadeOut, _fadeOutWaveform)) return;
            _fadeInWaveform = fadeIn;
            _fadeOutWaveform = fadeOut;
            OnPropertyChanged(nameof(FadeInWaveform));
            OnPropertyChanged(nameof(FadeOutWaveform));
            _fadeRevision++;
            OnPropertyChanged(nameof(FadeRevision));
        }

        /// <summary>Sets the clip's crossfade lengths (in beats) and refreshes the fade visual if they changed.</summary>
        public void SetFades(double fadeInBeats, double fadeOutBeats)
        {
            if (System.Math.Abs(fadeInBeats - _fadeInBeats) < 1e-9 &&
                System.Math.Abs(fadeOutBeats - _fadeOutBeats) < 1e-9) return;
            _fadeInBeats = fadeInBeats;
            _fadeOutBeats = fadeOutBeats;
            OnPropertyChanged(nameof(FadeInBeats));
            OnPropertyChanged(nameof(FadeOutBeats));
            OnPropertyChanged(nameof(FadeInWidth));
            OnPropertyChanged(nameof(FadeOutWidth));
            _fadeRevision++;
            OnPropertyChanged(nameof(FadeRevision));
        }

        /// <summary>Re-reads name/start/length/waveform after the model changes (edit or decode).</summary>
        public void RefreshFromModel()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Waveform));
            OnPropertyChanged(nameof(WaveStartFraction));
            OnPropertyChanged(nameof(WaveEndFraction));
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
                OnPropertyChanged(nameof(FadeInWidth));
                OnPropertyChanged(nameof(FadeOutWidth));
            }
        }
    }
}
