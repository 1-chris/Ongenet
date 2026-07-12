using Ongenet.App.ViewModels.Panels;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels
{
    /// <summary>
    /// The tabbed bottom panel, contextual to the selection. The first tab is the Instrument inspector
    /// normally, but becomes a Sample inspector when an audio sample clip is selected. Auto-switches to
    /// the Piano Roll tab for a MIDI clip, and to the Pattern tab for pattern tracks/clips.
    /// </summary>
    public class BottomPanelViewModel : ViewModelBase
    {
        private const int FirstTab = 0;
        private const int PatternTab = 1;
        private const int PianoRollTab = 2;
        private const int ClipTab = 3;
        private const int MidiFxTab = 4;
        private const int EffectsTab = 5;

        private readonly ISelectionService _selection;
        private int _selectedTabIndex;

        public BottomPanelViewModel(ISelectionService selection,
            InstrumentInspectorViewModel instrument, SampleInspectorViewModel sample,
            PianoRollViewModel pianoRoll, Panels.ChannelRackViewModel channelRack,
            PatternEditorViewModel patternEditor, ClipInspectorViewModel clipInspector,
            Panels.MidiFxViewModel midiFx, EffectsViewModel effects)
        {
            _selection = selection;
            Instrument = instrument;
            Sample = sample;
            PianoRoll = pianoRoll;
            ChannelRack = channelRack;
            PatternEditor = patternEditor;
            ClipInspector = clipInspector;
            MidiFx = midiFx;
            Effects = effects;
            _selection.SelectionChanged += OnSelectionChanged;
        }

        public InstrumentInspectorViewModel Instrument { get; }
        public SampleInspectorViewModel Sample { get; }
        public PianoRollViewModel PianoRoll { get; }
        public Panels.ChannelRackViewModel ChannelRack { get; }
        public PatternEditorViewModel PatternEditor { get; }
        public ClipInspectorViewModel ClipInspector { get; }
        public Panels.MidiFxViewModel MidiFx { get; }
        public EffectsViewModel Effects { get; }

        /// <summary>True when an audio sample clip is selected — the first tab shows the Sample inspector.</summary>
        public bool IsSampleSelected => _selection.SelectedClip is { IsAudio: true };

        /// <summary>True when the first tab should show the Instrument inspector.</summary>
        public bool IsInstrumentMode => !IsSampleSelected && _selection.SelectedTrack is { Kind: TrackKind.Instrument or TrackKind.Midi };

        /// <summary>Header of the contextual first tab.</summary>
        public string FirstTabHeader => IsSampleSelected ? "Sample" : "Instrument";

        private bool IsBusSelected => _selection.SelectedTrack is { IsBus: true };

        public bool IsPatternMode => _selection.SelectedPatternClip is not null
                                     || _selection.SelectedTrack is { Kind: TrackKind.Pattern };

        /// <summary>Whether the contextual first (Instrument/Sample) tab is shown.</summary>
        public bool ShowFirstTab => (IsSampleSelected || !IsBusSelected) && !IsPatternMode;

        /// <summary>Whether the Pattern editor tab is shown.</summary>
        public bool ShowPatternTab => IsPatternMode;

        /// <summary>Whether the Piano Roll tab is shown.</summary>
        public bool ShowPianoRollTab => !IsSampleSelected && !IsPatternMode
                                        && (_selection.SelectedClip is { IsMidi: true }
                                            || _selection.SelectedTrack is { Kind: TrackKind.Instrument or TrackKind.Midi });

        /// <summary>Whether the MIDI FX tab is shown.</summary>
        public bool ShowMidiFxTab => !IsSampleSelected && !IsPatternMode
                                     && _selection.SelectedTrack is { Kind: TrackKind.Instrument };

        /// <summary>Whether the Clip inspector tab is shown.</summary>
        public bool ShowClipTab => !IsPatternMode && _selection.SelectedClip is not null;

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetField(ref _selectedTabIndex, value);
        }

        public void BindPatternEditor(Pattern? pattern, Track? patternTrack)
        {
            PatternEditor.Bind(pattern);
            if (patternTrack is { Kind: TrackKind.Pattern, ActivePatternId: null } && pattern is not null)
                patternTrack.ActivePatternId = pattern.Id;
            OnPropertyChanged(nameof(IsPatternMode));
            OnPropertyChanged(nameof(ShowPatternTab));
            OnPropertyChanged(nameof(ShowFirstTab));
            OnPropertyChanged(nameof(ShowPianoRollTab));
            OnPropertyChanged(nameof(ShowMidiFxTab));
            OnPropertyChanged(nameof(ShowClipTab));
        }

        public void ShowPatternEditor()
        {
            if (ShowPatternTab)
                SelectedTabIndex = PatternTab;
        }

        /// <summary>Legacy hook — pattern editing uses the bottom Pattern tab.</summary>
        public void ShowChannelRack() => ShowPatternEditor();

        private void OnSelectionChanged()
        {
            OnPropertyChanged(nameof(IsSampleSelected));
            OnPropertyChanged(nameof(IsInstrumentMode));
            OnPropertyChanged(nameof(FirstTabHeader));
            OnPropertyChanged(nameof(IsPatternMode));
            OnPropertyChanged(nameof(ShowFirstTab));
            OnPropertyChanged(nameof(ShowPatternTab));
            OnPropertyChanged(nameof(ShowPianoRollTab));
            OnPropertyChanged(nameof(ShowMidiFxTab));
            OnPropertyChanged(nameof(ShowClipTab));

            if (IsPatternMode)
            {
                SelectedTabIndex = PatternTab;
                return;
            }

            if (!ShowFirstTab && !ShowPianoRollTab && !ShowMidiFxTab)
            {
                SelectedTabIndex = EffectsTab;
                return;
            }

            switch (_selection.SelectedClip)
            {
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
