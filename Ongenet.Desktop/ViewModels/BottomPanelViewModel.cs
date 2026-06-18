using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// The tabbed bottom panel, contextual to the selection. The first tab is the Instrument inspector
    /// normally, but becomes a Sample inspector when an audio sample clip is selected. Auto-switches to
    /// the Piano Roll tab for a MIDI clip, and to the first (Sample) tab for an audio clip.
    /// </summary>
    public class BottomPanelViewModel : ViewModelBase
    {
        private const int FirstTab = 0;
        private const int PianoRollTab = 1;

        private readonly ISelectionService _selection;
        private int _selectedTabIndex;

        public BottomPanelViewModel(ISelectionService selection,
            InstrumentInspectorViewModel instrument, SampleInspectorViewModel sample,
            PianoRollViewModel pianoRoll, EffectsViewModel effects)
        {
            _selection = selection;
            Instrument = instrument;
            Sample = sample;
            PianoRoll = pianoRoll;
            Effects = effects;
            _selection.SelectionChanged += OnSelectionChanged;
        }

        public InstrumentInspectorViewModel Instrument { get; }
        public SampleInspectorViewModel Sample { get; }
        public PianoRollViewModel PianoRoll { get; }
        public EffectsViewModel Effects { get; }

        /// <summary>True when an audio sample clip is selected — the first tab shows the Sample inspector.</summary>
        public bool IsSampleSelected => _selection.SelectedClip is { IsAudio: true };

        /// <summary>True when the first tab should show the Instrument inspector.</summary>
        public bool IsInstrumentMode => !IsSampleSelected;

        /// <summary>Header of the contextual first tab.</summary>
        public string FirstTabHeader => IsSampleSelected ? "Sample" : "Instrument";

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetField(ref _selectedTabIndex, value);
        }

        private void OnSelectionChanged()
        {
            OnPropertyChanged(nameof(IsSampleSelected));
            OnPropertyChanged(nameof(IsInstrumentMode));
            OnPropertyChanged(nameof(FirstTabHeader));

            switch (_selection.SelectedClip)
            {
                // Jump to the piano roll for a MIDI clip, or the Sample inspector for an audio clip.
                case { IsMidi: true }:
                    SelectedTabIndex = PianoRollTab;
                    break;
                case { IsAudio: true }:
                    SelectedTabIndex = FirstTab;
                    break;
            }
        }
    }
}
