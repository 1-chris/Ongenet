namespace Ongenet.Desktop.ViewModels.PianoRoll
{
    /// <summary>One key/row of the piano roll: drives both the left key gutter and the row shading.</summary>
    public class PianoKeyViewModel : ViewModelBase
    {
        private static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        private static readonly bool[] Black = { false, true, false, true, false, false, true, false, true, false, true, false };

        private bool _isActive;

        public PianoKeyViewModel(int midiNote, PianoRollMetrics metrics)
        {
            MidiNote = midiNote;
            var pitchClass = midiNote % 12;
            IsBlack = Black[pitchClass];
            IsC = pitchClass == 0;
            Name = $"{Names[pitchClass]}{midiNote / 12 - 1}";
            Top = metrics.NoteToY(midiNote);
        }

        public int MidiNote { get; }
        public string Name { get; }
        public bool IsBlack { get; }

        /// <summary>True for C notes — used to draw octave label and a stronger row line.</summary>
        public bool IsC { get; }

        /// <summary>Top pixel of this row.</summary>
        public double Top { get; }

        /// <summary>Row height (constant).</summary>
        public double Height => PianoRollMetrics.KeyHeight;

        /// <summary>True while this note is sounding (highlights the key).</summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetField(ref _isActive, value);
        }
    }
}
