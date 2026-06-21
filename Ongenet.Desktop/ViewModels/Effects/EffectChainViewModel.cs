using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Services;

namespace Ongenet.Desktop.ViewModels.Effects
{
    /// <summary>
    /// A reusable, self-contained editor for one insert-effect chain — the list of effect cards, the
    /// grouped "Add effect" menu, and add/remove/reorder/bypass with undo. It operates on a supplied
    /// backing <see cref="List{IAudioEffect}"/> plus a commit delegate, so the same control drives both a
    /// track's (post) chain and an individual instrument slot's (pre) chain. Created fresh whenever the
    /// edited target changes; dispose-free (only weak-ish event hookups it owns).
    /// </summary>
    public sealed class EffectChainViewModel : ViewModelBase
    {
        private readonly List<IAudioEffect> _effects;
        private readonly Action _commit;        // publishes the list to the audio thread (e.g. Track.CommitEffects)
        private readonly Action _changed;       // notifies the engine to re-prepare (publish TracksChangedEvent)
        private readonly IEffectRegistry _registry;
        private readonly IHistoryService _history;
        private readonly ITransportService _transport;

        // Preferred display order for the add-effect menu categories.
        private static readonly string[] CategoryOrder =
            { "Dynamics", "EQ & Filter", "Modulation", "Delay & Reverb", "Distortion", "Pitch", "Utility", "Plugins" };

        public EffectChainViewModel(List<IAudioEffect> effects, Action commit, Action changed,
            IEffectRegistry registry, IHistoryService history, ITransportService transport, IPlaybackClock clock)
        {
            _effects = effects;
            _commit = commit;
            _changed = changed;
            _registry = registry;
            _history = history;
            _transport = transport;

            _registry.Changed += () => Dispatcher.UIThread.Post(RebuildAddable);
            clock.Tick += OnPlaybackTick;

            RebuildAddable();
            Rebuild();
        }

        /// <summary>The effect cards in chain order.</summary>
        public ObservableCollection<EffectViewModel> Effects { get; } = new();

        /// <summary>The "Add effect" menu, grouped by category.</summary>
        public IReadOnlyList<EffectCategoryViewModel> AddableCategories { get; private set; } =
            new List<EffectCategoryViewModel>();

        /// <summary>True when the chain has at least one effect (drives the chain caption).</summary>
        public bool HasEffects => Effects.Count > 0;

        // While playing, re-read each effect's enabled state + parameters so automation shows live.
        private void OnPlaybackTick()
        {
            if (_transport.State != TransportState.Playing) return;
            foreach (var fx in Effects) fx.Refresh();
        }

        /// <summary>Refreshes the open/close button of the effect backed by <paramref name="editor"/>.</summary>
        public void RefreshEditor(IPluginEditor editor)
        {
            foreach (var vm in Effects)
                if (ReferenceEquals(vm.Editor, editor)) { vm.NotifyEditorState(); return; }
        }

        private void RebuildAddable()
        {
            int Rank(string category)
            {
                var i = Array.IndexOf(CategoryOrder, category);
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

        /// <summary>Adds an effect of the given type to the end of the chain (used by the menu + drag-drop).</summary>
        public void AddEffect(string id)
        {
            if (CreateEffect(id) is { } fx) Apply("Add effect", () => _effects.Add(fx));
        }

        /// <summary>Inserts an effect of the given type at <paramref name="index"/> (zoned drag-drop).</summary>
        public void InsertEffect(int index, string id)
        {
            if (CreateEffect(id) is { } fx) Apply("Add effect", () => _effects.Insert(Clamp(index), fx));
        }

        /// <summary>Replaces the effect at <paramref name="index"/> with a new one (zoned drag-drop).</summary>
        public void ReplaceEffectAt(int index, string id)
        {
            if (InRange(index) && CreateEffect(id) is { } fx) Apply("Replace effect", () => _effects[index] = fx);
        }

        /// <summary>Adds an effect loaded from a dropped <c>.ongenpreset</c> to the end of the chain.</summary>
        public void AddEffectPreset(string presetPath)
        {
            if (LoadEffectPreset(presetPath) is { } fx) Apply("Add effect preset", () => _effects.Add(fx));
        }

        /// <summary>Inserts a dropped effect preset at <paramref name="index"/> (zoned drag-drop).</summary>
        public void InsertEffectPreset(int index, string presetPath)
        {
            if (LoadEffectPreset(presetPath) is { } fx) Apply("Add effect preset", () => _effects.Insert(Clamp(index), fx));
        }

        /// <summary>Replaces the effect at <paramref name="index"/> with a dropped effect preset (zoned drag-drop).</summary>
        public void ReplaceEffectPresetAt(int index, string presetPath)
        {
            if (InRange(index) && LoadEffectPreset(presetPath) is { } fx) Apply("Replace effect", () => _effects[index] = fx);
        }

        /// <summary>Position of an effect card in the chain (used by the view to map a drop to an index).</summary>
        public int IndexOf(EffectViewModel vm) => _effects.IndexOf(vm.Effect);

        private IAudioEffect? CreateEffect(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try { return _registry.Create(id); }
            catch { return null; }
        }

        private IAudioEffect? LoadEffectPreset(string presetPath)
        {
            var instruments = App.ServiceProvider?.GetService<IInstrumentRegistry>();
            if (instruments is null) return null;
            try
            {
                using var fs = System.IO.File.OpenRead(presetPath);
                return Ongenet.Core.Persistence.PresetFile.Load(fs, instruments, _registry)?.Effect;
            }
            catch { return null; }
        }

        private int Clamp(int index) => Math.Clamp(index, 0, _effects.Count);
        private bool InRange(int index) => index >= 0 && index < _effects.Count;

        // Captures undo, mutates the chain, then commits to the audio thread + rebuilds the cards.
        private void Apply(string historyLabel, Action mutate)
        {
            _history.Capture(historyLabel);
            mutate();
            _commit();
            _changed();
            Rebuild();
        }

        /// <summary>Appends every effect from a dropped FX-chain preset to the end of this chain.</summary>
        public void AddEffectChainPreset(string presetPath)
        {
            var instruments = App.ServiceProvider?.GetService<IInstrumentRegistry>();
            if (instruments is null) return;

            IReadOnlyList<IAudioEffect>? chain;
            try
            {
                using var fs = System.IO.File.OpenRead(presetPath);
                chain = Ongenet.Core.Persistence.PresetFile.Load(fs, instruments, _registry)?.Effects;
            }
            catch { return; }
            if (chain is null || chain.Count == 0) return;

            _history.Capture("Add FX chain preset");
            foreach (var fx in chain) _effects.Add(fx);
            _commit();
            _changed();
            Rebuild();
        }

        private string _chainPresetName = string.Empty;

        /// <summary>The name typed into the "Save chain" flyout.</summary>
        public string ChainPresetName
        {
            get => _chainPresetName;
            set => SetField(ref _chainPresetName, value);
        }

        /// <summary>Saves the current chain (all its effects, in order) as an FX-chain preset.</summary>
        public void SaveChainAsPreset()
        {
            if (_effects.Count == 0) return;
            var name = string.IsNullOrWhiteSpace(_chainPresetName) ? "FX Chain" : _chainPresetName.Trim();
            App.ServiceProvider?.GetService<IPresetLibrary>()?.SaveChain(_effects, name);
            ChainPresetName = string.Empty;
        }

        private void RemoveEffect(EffectViewModel vm)
        {
            _history.Capture("Remove effect");
            _effects.Remove(vm.Effect);
            _commit();
            _changed();
            Rebuild();
        }

        private void MoveEffect(EffectViewModel vm, int delta)
        {
            var index = _effects.IndexOf(vm.Effect);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= _effects.Count) return;
            _history.Capture("Reorder effect");

            _effects.RemoveAt(index);
            _effects.Insert(target, vm.Effect);
            _commit();
            _changed();
            Rebuild();
        }

        private void MoveUp(EffectViewModel vm) => MoveEffect(vm, -1);
        private void MoveDown(EffectViewModel vm) => MoveEffect(vm, +1);

        private void Rebuild()
        {
            Effects.Clear();
            foreach (var effect in _effects)
            {
                Effects.Add(effect switch
                {
                    EqEffect eq => new EqEffectViewModel(eq, RemoveEffect, MoveUp, MoveDown),
                    FilterEffect filter => new FilterEffectViewModel(filter, RemoveEffect, MoveUp, MoveDown),
                    SidechainEffect sc => new SidechainEffectViewModel(sc, RemoveEffect, MoveUp, MoveDown),
                    StutteroEffect st => new StutteroEffectViewModel(st, RemoveEffect, MoveUp, MoveDown),
                    VocoderEffect vc => new VocoderEffectViewModel(vc, RemoveEffect, MoveUp, MoveDown),
                    _ => new EffectViewModel(effect, RemoveEffect, MoveUp, MoveDown)
                });
            }

            for (var i = 0; i < Effects.Count; i++)
            {
                Effects[i].Position = i + 1;
                Effects[i].IsFirst = i == 0;
                Effects[i].IsLast = i == Effects.Count - 1;
            }

            OnPropertyChanged(nameof(HasEffects));
        }
    }
}
