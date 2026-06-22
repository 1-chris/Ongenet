using System.ComponentModel;
using Ongenet.Core.Models.Audio;

namespace Ongenet.App.ViewModels.PianoRoll
{
    /// <summary>
    /// View model for one note rectangle in the piano roll. Maps the underlying
    /// <see cref="MidiNote"/> (clip-relative beats + pitch) to pixel position/size through the
    /// shared <see cref="PianoRollMetrics"/>.
    /// </summary>
    public class NoteViewModel : ViewModelBase
    {
        private readonly PianoRollMetrics _metrics;
        private bool _isSelected;

        public NoteViewModel(MidiNote model, PianoRollMetrics metrics)
        {
            Model = model;
            _metrics = metrics;
            _metrics.PropertyChanged += OnMetricsChanged;
        }

        public MidiNote Model { get; }

        public double Left => _metrics.BeatsToPixels(Model.StartBeat);
        public double Width => _metrics.BeatsToPixels(Model.LengthBeats);
        public double Top => _metrics.NoteToY(Model.Note);
        public double Height => PianoRollMetrics.KeyHeight - 1;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        /// <summary>Re-reads position/size after the model changes.</summary>
        public void RefreshFromModel()
        {
            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Top));
        }

        private void OnMetricsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PianoRollMetrics.PixelsPerBeat))
            {
                OnPropertyChanged(nameof(Left));
                OnPropertyChanged(nameof(Width));
            }
        }
    }
}
