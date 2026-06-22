using System;
using Ongenet.App.Controls;

namespace Ongenet.App.ViewModels.Timeline
{
    /// <summary>
    /// The single source of truth for the timeline's time&lt;-&gt;pixel mapping: zoom
    /// (<see cref="PixelsPerBeat"/>), arrange length (<see cref="TotalBeats"/>), bar length
    /// (<see cref="BeatsPerBar"/>) and the horizontal scroll offset.
    /// </summary>
    public class TimelineMetrics : ViewModelBase
    {
        private double _pixelsPerBeat = 16.0;
        private double _totalBeats = 128.0;
        private double _horizontalOffset;
        private int _beatsPerBar = 4;

        /// <summary>Horizontal zoom: how many pixels one beat occupies.</summary>
        public double PixelsPerBeat
        {
            get => _pixelsPerBeat;
            set
            {
                // Clamp to a sane zoom range.
                var clamped = value < 2.0 ? 2.0 : value > 200.0 ? 200.0 : value;
                if (SetField(ref _pixelsPerBeat, clamped))
                {
                    OnPropertyChanged(nameof(TotalWidth));
                    OnPropertyChanged(nameof(SnapBeats));
                }
            }
        }

        /// <summary>Beats per bar (from the project time signature).</summary>
        public int BeatsPerBar
        {
            get => _beatsPerBar;
            set
            {
                if (SetField(ref _beatsPerBar, value < 1 ? 1 : value))
                {
                    OnPropertyChanged(nameof(SnapBeats));
                }
            }
        }

        /// <summary>The current zoom-adaptive snap/grid size, in beats.</summary>
        public double SnapBeats => GridMath.SnapBeats(_pixelsPerBeat, _beatsPerBar);

        /// <summary>Snaps a beat value to the current grid.</summary>
        public double Snap(double beat)
        {
            var step = SnapBeats;
            return step <= 0 ? beat : Math.Round(beat / step) * step;
        }

        /// <summary>Total arrange length, in beats.</summary>
        public double TotalBeats
        {
            get => _totalBeats;
            set
            {
                if (SetField(ref _totalBeats, value))
                {
                    OnPropertyChanged(nameof(TotalWidth));
                }
            }
        }

        /// <summary>Current horizontal scroll offset, in pixels (driven by the lanes scroll viewer).</summary>
        public double HorizontalOffset
        {
            get => _horizontalOffset;
            set => SetField(ref _horizontalOffset, value);
        }

        /// <summary>Total width of the arrange area, in pixels.</summary>
        public double TotalWidth => BeatsToPixels(_totalBeats);

        /// <summary>Converts a position/length in beats to pixels.</summary>
        public double BeatsToPixels(double beats) => beats * _pixelsPerBeat;

        /// <summary>Converts a position/length in pixels to beats.</summary>
        public double PixelsToBeats(double pixels) => pixels / _pixelsPerBeat;
    }
}
