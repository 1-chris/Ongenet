using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// The tabbed bottom panel (Instrument / Piano Roll / Effects), contextual to the selected
    /// track. Auto-switches to the Piano Roll tab when a MIDI clip is selected.
    /// </summary>
    public class BottomPanelViewModel : ViewModelBase
    {
        private const int PianoRollTab = 1;

        private readonly ISelectionService _selection;
        private int _selectedTabIndex;

        public BottomPanelViewModel(ISelectionService selection,
            InstrumentInspectorViewModel instrument, PianoRollViewModel pianoRoll, EffectsViewModel effects)
        {
            _selection = selection;
            Instrument = instrument;
            PianoRoll = pianoRoll;
            Effects = effects;
            _selection.SelectionChanged += OnSelectionChanged;
        }

        public InstrumentInspectorViewModel Instrument { get; }
        public PianoRollViewModel PianoRoll { get; }
        public EffectsViewModel Effects { get; }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetField(ref _selectedTabIndex, value);
        }

        private void OnSelectionChanged()
        {
            // Jump to the piano roll when a MIDI clip is selected (e.g. just created).
            if (_selection.SelectedClip is { IsMidi: true })
            {
                SelectedTabIndex = PianoRollTab;
            }
        }
    }
}
