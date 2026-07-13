using System.ComponentModel;
using Ongenet.App.Controls;

namespace Ongenet.App.ViewModels.Timeline
{
    /// <summary>
    /// A single bar marker on the timeline ruler. Its pixel position is derived from the bar's
    /// beat position through the shared <see cref="TimelineMetrics"/> and re-raised on zoom.
    /// </summary>
    public class BarTickViewModel : ViewModelBase
    {
        private readonly TimelineMetrics _metrics;

        public BarTickViewModel(int barNumber, TimelineMetrics metrics)
        {
            BarNumber = barNumber;
            _metrics = metrics;
            _metrics.PropertyChanged += OnMetricsChanged;
        }

        /// <summary>1-based bar number shown as the label.</summary>
        public int BarNumber { get; }

        /// <summary>Beat position of this bar, derived live from the current bar length so it tracks
        /// time-signature changes.</summary>
        private double Beat => (BarNumber - 1) * _metrics.BeatsPerBar;

        /// <summary>Left edge of the tick on the ruler canvas, in pixels.</summary>
        public double Left => _metrics.BeatsToPixels(Beat);

        /// <summary>Whether the bar number is shown at the current zoom (thins labels when zoomed out).</summary>
        public bool IsLabelVisible
        {
            get
            {
                var stride = GridMath.BarLabelStride(_metrics.PixelsPerBeat, _metrics.BeatsPerBar);
                return BarNumber == 1 || (BarNumber - 1) % stride == 0;
            }
        }

        private void OnMetricsChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Reposition on both zoom (PixelsPerBeat) and time-signature (BeatsPerBar) changes.
            if (e.PropertyName is nameof(TimelineMetrics.PixelsPerBeat) or nameof(TimelineMetrics.BeatsPerBar))
            {
                OnPropertyChanged(nameof(Left));
                OnPropertyChanged(nameof(IsLabelVisible));
            }
        }
    }
}
