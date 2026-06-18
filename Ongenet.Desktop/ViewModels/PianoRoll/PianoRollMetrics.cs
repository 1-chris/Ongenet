using System;
using Ongenet.Desktop.Controls;

namespace Ongenet.Desktop.ViewModels.PianoRoll
{
    /// <summary>
    /// Time↔pixel and pitch↔pixel mapping for the piano roll — the editor's own metrics object,
    /// analogous to the arrange view's <c>TimelineMetrics</c>. The horizontal axis is clip-local
    /// beats; the vertical axis is pitch, one row per semitone from <see cref="HighNote"/> (top)
    /// down to <see cref="LowNote"/>.
    /// </summary>
    public class PianoRollMetrics : ViewModelBase
    {
        /// <summary>Lowest note row (A0).</summary>
        public const int LowNote = 21;

        /// <summary>Highest note row (C8).</summary>
        public const int HighNote = 108;

        /// <summary>Height of one note row, in pixels.</summary>
        public const double KeyHeight = 14.0;

        private double _pixelsPerBeat = 40.0;
        private double _totalBeats = 4.0;
        private int _beatsPerBar = 4;

        /// <summary>Number of note rows.</summary>
        public int NoteCount => HighNote - LowNote + 1;

        /// <summary>Total grid height, in pixels.</summary>
        public double TotalHeight => NoteCount * KeyHeight;

        /// <summary>Horizontal zoom: pixels per beat.</summary>
        public double PixelsPerBeat
        {
            get => _pixelsPerBeat;
            set
            {
                var clamped = value < 8.0 ? 8.0 : value > 400.0 ? 400.0 : value;
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

        /// <summary>Clip length in beats (the editor's horizontal extent).</summary>
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

        /// <summary>Total grid width, in pixels.</summary>
        public double TotalWidth => PixelsPerBeat * TotalBeats;

        public double BeatsToPixels(double beats) => beats * PixelsPerBeat;

        public double PixelsToBeats(double pixels) => PixelsPerBeat > 0 ? pixels / PixelsPerBeat : 0;

        /// <summary>Top pixel of a note's row (row 0 = highest note).</summary>
        public double NoteToY(int note) => (HighNote - note) * KeyHeight;

        /// <summary>The note number at a vertical pixel position.</summary>
        public int YToNote(double y)
        {
            var note = HighNote - (int)Math.Floor(y / KeyHeight);
            if (note < LowNote) note = LowNote;
            if (note > HighNote) note = HighNote;
            return note;
        }

        /// <summary>Snaps a beat value to the editor grid.</summary>
        public double Snap(double beat)
        {
            var step = SnapBeats;
            if (step <= 0) return beat < 0 ? 0 : beat;
            var snapped = Math.Round(beat / step) * step;
            return snapped < 0 ? 0 : snapped;
        }
    }
}
