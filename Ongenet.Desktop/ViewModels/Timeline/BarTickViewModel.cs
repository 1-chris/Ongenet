using System.ComponentModel;

namespace Ongenet.Desktop.ViewModels.Timeline
{
    /// <summary>
    /// A single bar marker on the timeline ruler. Its pixel position is derived from the bar's
    /// beat position through the shared <see cref="TimelineMetrics"/> and re-raised on zoom.
    /// </summary>
    public class BarTickViewModel : ViewModelBase
    {
        private readonly TimelineMetrics _metrics;
        private readonly double _beat;

        public BarTickViewModel(int barNumber, double beat, TimelineMetrics metrics)
        {
            BarNumber = barNumber;
            _beat = beat;
            _metrics = metrics;
            _metrics.PropertyChanged += OnMetricsChanged;
        }

        /// <summary>1-based bar number shown as the label.</summary>
        public int BarNumber { get; }

        /// <summary>Left edge of the tick on the ruler canvas, in pixels.</summary>
        public double Left => _metrics.BeatsToPixels(_beat);

        private void OnMetricsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimelineMetrics.PixelsPerBeat))
            {
                OnPropertyChanged(nameof(Left));
            }
        }
    }
}
