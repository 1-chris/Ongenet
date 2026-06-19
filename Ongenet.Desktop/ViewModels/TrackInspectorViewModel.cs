using System.Collections.Generic;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Services;

namespace Ongenet.Desktop.ViewModels
{
    /// <summary>
    /// Left-hand inspector for the selected track: name, mute/solo, volume, pan and colour.
    /// Edits mutate the underlying <see cref="Track"/> and publish a <see cref="TrackChangedEvent"/>
    /// so the timeline lane reflects them.
    /// </summary>
    public class TrackInspectorViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly IEventAggregator _events;
        private readonly ITransportService _transport;
        private readonly IHistoryService _history;

        public TrackInspectorViewModel(ISelectionService selection, IEventAggregator events,
            ITransportService transport, IPlaybackClock clock, IHistoryService history)
        {
            _selection = selection;
            _events = events;
            _transport = transport;
            _history = history;
            _selection.SelectionChanged += OnSelectionChanged;
            // Stay in sync when the track is edited elsewhere (e.g. header mute/solo toggles).
            _events.Subscribe<TrackChangedEvent>(e =>
            {
                if (ReferenceEquals(e.Track, Track)) OnSelectionChanged();
            });
            // Reflect automation moving Volume/Pan live during playback.
            clock.Tick += OnPlaybackTick;
        }

        // While playing, re-read Volume/Pan so automation visibly moves the sliders.
        private void OnPlaybackTick()
        {
            if (Track is null || _transport.State != TransportState.Playing) return;
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(Pan));
        }

        private Track? Track => _selection.SelectedTrack;

        /// <summary>Whether a track is selected (controls visible vs empty-state).</summary>
        public bool HasTrack => Track is not null;

        /// <summary>The available track colours, as Catppuccin palette keys.</summary>
        public IReadOnlyList<string> ColorKeys { get; } = new[]
        {
            "CatppuccinRed", "CatppuccinPeach", "CatppuccinYellow", "CatppuccinGreen",
            "CatppuccinTeal", "CatppuccinSky", "CatppuccinBlue", "CatppuccinMauve",
            "CatppuccinPink", "CatppuccinLavender"
        };

        public string Name
        {
            get => Track?.Name ?? string.Empty;
            set
            {
                if (Track is null || Track.Name == value) return;
                _history.Capture("Rename track");
                Track.Name = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public bool IsMuted
        {
            get => Track?.IsMuted ?? false;
            set
            {
                if (Track is null || Track.IsMuted == value) return;
                _history.Capture(value ? "Mute track" : "Unmute track");
                Track.IsMuted = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public bool IsSoloed
        {
            get => Track?.IsSoloed ?? false;
            set
            {
                if (Track is null || Track.IsSoloed == value) return;
                _history.Capture(value ? "Solo track" : "Unsolo track");
                Track.IsSoloed = value;
                OnPropertyChanged();
                Notify();
            }
        }

        public double Volume
        {
            get => Track?.Volume ?? 0.0;
            set
            {
                if (Track is null || Track.Volume == value) return;
                Track.Volume = value;
                OnPropertyChanged();
            }
        }

        public double Pan
        {
            get => Track?.Pan ?? 0.0;
            set
            {
                if (Track is null || Track.Pan == value) return;
                Track.Pan = value;
                OnPropertyChanged();
            }
        }

        public string ColorKey
        {
            get => Track?.ColorKey ?? "CatppuccinMauve";
            set
            {
                if (Track is null || value is null || Track.ColorKey == value) return;
                _history.Capture("Change track colour");
                Track.ColorKey = value;
                OnPropertyChanged();
                Notify();
            }
        }

        private void Notify()
        {
            if (Track is not null) _events.Publish(new TrackChangedEvent(Track));
        }

        private void OnSelectionChanged()
        {
            // The selected track changed underneath us: re-read every field.
            OnPropertyChanged(nameof(HasTrack));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(IsSoloed));
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(Pan));
            OnPropertyChanged(nameof(ColorKey));
        }
    }
}
