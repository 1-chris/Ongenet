using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Services;
using Ongenet.Desktop.ViewModels.Effects;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// Effects tab for the selected track: the track-level (post) insert chain that processes the summed
    /// output of every instrument in the rack. The chain editor itself lives in the reusable
    /// <see cref="EffectChainViewModel"/> (shared with each instrument slot's own pre-chain).
    /// </summary>
    public class EffectsViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly IEffectRegistry _registry;
        private readonly IEventAggregator _events;
        private readonly ITransportService _transport;
        private readonly IPlaybackClock _clock;
        private readonly IHistoryService _history;

        public EffectsViewModel(ISelectionService selection, IEffectRegistry registry,
            IEventAggregator events, ITransportService transport, IPlaybackClock clock, IHistoryService history)
        {
            _selection = selection;
            _registry = registry;
            _events = events;
            _transport = transport;
            _clock = clock;
            _history = history;
            _selection.SelectionChanged += OnSelectionChanged;
            Rebuild();
        }

        private Track? Track => _selection.SelectedTrack;

        public bool HasTrack => Track is not null;
        public string TrackName => Track?.Name ?? string.Empty;

        /// <summary>The selected track's (post) effect chain editor, or null when no track is selected.</summary>
        public EffectChainViewModel? Chain { get; private set; }

        /// <summary>Refreshes the open/close button of the effect backed by <paramref name="editor"/>.</summary>
        public void RefreshEditor(IPluginEditor editor) => Chain?.RefreshEditor(editor);

        private void Rebuild()
        {
            Chain = Track is { } track
                ? new EffectChainViewModel(track.Effects, track.CommitEffects,
                    () => _events.Publish(new TracksChangedEvent()), _registry, _history, _transport, _clock)
                : null;
            OnPropertyChanged(nameof(Chain));
        }

        private void OnSelectionChanged()
        {
            Rebuild();
            OnPropertyChanged(nameof(HasTrack));
            OnPropertyChanged(nameof(TrackName));
        }
    }
}
