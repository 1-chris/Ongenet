using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
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
    /// Effects tab for the selected track: lists the track's insert effects (each with its
    /// parameters and a remove button) and offers an "Add effect" menu of available effects.
    /// Works for instrument and audio tracks alike.
    /// </summary>
    public class EffectsViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly IEffectRegistry _registry;
        private readonly IEventAggregator _events;
        private readonly ITransportService _transport;

        public EffectsViewModel(ISelectionService selection, IEffectRegistry registry,
            IEventAggregator events, ITransportService transport, IPlaybackClock clock)
        {
            _selection = selection;
            _registry = registry;
            _events = events;
            _transport = transport;
            _selection.SelectionChanged += OnSelectionChanged;
            // Discovered CLAP effects appear here as they're registered (off the scan thread).
            _registry.Changed += () => Dispatcher.UIThread.Post(RebuildAddable);
            // Reflect automation moving effect knobs / on-off state live during playback.
            clock.Tick += OnPlaybackTick;

            RebuildAddable();
            Rebuild();
        }

        // While playing, re-read each effect's enabled state + parameters so automation shows live.
        private void OnPlaybackTick()
        {
            if (_transport.State != TransportState.Playing) return;
            foreach (var fx in Effects) fx.Refresh();
        }

        // Preferred display order for the add-effect menu categories.
        private static readonly string[] CategoryOrder =
            { "Dynamics", "EQ & Filter", "Modulation", "Delay & Reverb", "Distortion", "Utility", "Plugins" };

        /// <summary>The "Add effect" menu, grouped by category (each entry adds its effect to the track).</summary>
        public IReadOnlyList<EffectCategoryViewModel> AddableCategories { get; private set; } =
            new List<EffectCategoryViewModel>();

        // Rebuilds the add-effect menu from the registry (built-ins + discovered CLAP effects), grouped.
        private void RebuildAddable()
        {
            int Rank(string category)
            {
                var i = System.Array.IndexOf(CategoryOrder, category);
                return i < 0 ? CategoryOrder.Length : i;
            }

            AddableCategories = _registry.Available
                .GroupBy(info => info.Category)
                .OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key)
                .Select(g => new EffectCategoryViewModel(g.Key,
                    g.Select(info => new AvailableEffectViewModel(
                        info.DisplayName, new RelayCommand(() => AddEffect(info.Id)))).ToList()))
                .ToList();

            OnPropertyChanged(nameof(AddableCategories));
        }

        /// <summary>Refreshes the open/close button of the effect backed by <paramref name="editor"/>.</summary>
        public void RefreshEditor(IPluginEditor editor)
        {
            foreach (var vm in Effects)
                if (ReferenceEquals(vm.Editor, editor)) { vm.NotifyEditorState(); return; }
        }

        private Track? Track => _selection.SelectedTrack;

        public bool HasTrack => Track is not null;
        public string TrackName => Track?.Name ?? string.Empty;

        /// <summary>The selected track's effect chain.</summary>
        public ObservableCollection<EffectViewModel> Effects { get; } = new();

        private void AddEffect(string id)
        {
            if (Track is not { } track || string.IsNullOrEmpty(id)) return;
            var effect = _registry.Create(id);
            track.Effects.Add(effect);
            track.CommitEffects();
            _events.Publish(new TracksChangedEvent()); // engine prepares + picks up the chain
            Rebuild();
        }

        private void RemoveEffect(EffectViewModel vm)
        {
            if (Track is not { } track) return;
            track.Effects.Remove(vm.Effect);
            track.CommitEffects();
            _events.Publish(new TracksChangedEvent());
            Rebuild();
        }

        // Reorders an effect in the chain (the engine processes track.Effects in order).
        private void MoveEffect(EffectViewModel vm, int delta)
        {
            if (Track is not { } track) return;
            var index = track.Effects.IndexOf(vm.Effect);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= track.Effects.Count) return;

            track.Effects.RemoveAt(index);
            track.Effects.Insert(target, vm.Effect);
            track.CommitEffects();
            _events.Publish(new TracksChangedEvent());
            Rebuild();
        }

        private void Rebuild()
        {
            Effects.Clear();
            if (Track is { } track)
            {
                foreach (var effect in track.Effects)
                {
                    Effects.Add(effect switch
                    {
                        EqEffect eq => new EqEffectViewModel(eq, RemoveEffect, MoveUp, MoveDown),
                        FilterEffect filter => new FilterEffectViewModel(filter, RemoveEffect, MoveUp, MoveDown),
                        _ => new EffectViewModel(effect, RemoveEffect, MoveUp, MoveDown)
                    });
                }
            }

            for (var i = 0; i < Effects.Count; i++)
            {
                Effects[i].Position = i + 1;
                Effects[i].IsFirst = i == 0;
                Effects[i].IsLast = i == Effects.Count - 1;
            }

            OnPropertyChanged(nameof(HasEffects));
        }

        private void MoveUp(EffectViewModel vm) => MoveEffect(vm, -1);
        private void MoveDown(EffectViewModel vm) => MoveEffect(vm, +1);

        /// <summary>True when the selected track has at least one effect (drives the chain caption).</summary>
        public bool HasEffects => Effects.Count > 0;

        private void OnSelectionChanged()
        {
            Rebuild();
            OnPropertyChanged(nameof(HasTrack));
            OnPropertyChanged(nameof(TrackName));
        }
    }
}
