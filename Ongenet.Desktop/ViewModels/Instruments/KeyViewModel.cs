namespace Ongenet.Desktop.ViewModels.Instruments
{
    /// <summary>One key on the on-screen mini-keyboard.</summary>
    public class KeyViewModel : ViewModelBase
    {
        private bool _isActive;

        public KeyViewModel(int midiNote, string label, bool isBlack)
        {
            MidiNote = midiNote;
            Label = label;
            IsBlack = isBlack;
        }

        /// <summary>MIDI note number this key plays.</summary>
        public int MidiNote { get; }

        /// <summary>Note name (e.g. "C4").</summary>
        public string Label { get; }

        /// <summary>Whether this is a black (accidental) key, for styling.</summary>
        public bool IsBlack { get; }

        /// <summary>True while this note is sounding (highlights the key).</summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetField(ref _isActive, value);
        }
    }
}
