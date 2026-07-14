using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;
using Ongenet.App.ViewModels;

namespace Ongenet.App.ViewModels.Panels
{
    /// <summary>MIDI FX tab: per-track MIDI effect chain editor.</summary>
    public class MidiFxViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly IEventAggregator _events;
        private readonly IHistoryService _history;
        private readonly IMidiEffectRegistry _registry;

        public MidiFxViewModel(
            ISelectionService selection,
            IEventAggregator events,
            IHistoryService history,
            IMidiEffectRegistry registry)
        {
            _selection = selection;
            _events = events;
            _history = history;
            _registry = registry;
            _selection.SelectionChanged += OnSelectionChanged;
            _registry.Changed += RebuildAvailable;

            Available = new ObservableCollection<AvailableMidiEffectViewModel>();
            RebuildAvailable();
        }

        public ObservableCollection<AvailableMidiEffectViewModel> Available { get; }

        public ObservableCollection<MidiEffectSlotViewModel> Effects { get; } = new();

        private Track? Track => _selection.SelectedTrack;

        public bool HasTrack => Track is { IsBus: false };

        public string TrackName => Track?.Name ?? string.Empty;

        private void RebuildAvailable()
        {
            Available.Clear();
            foreach (var info in _registry.Available.OrderBy(i => i.Category).ThenBy(i => i.DisplayName))
                Available.Add(new AvailableMidiEffectViewModel(info, AddById));
        }

        private void AddById(string id)
        {
            if (Track is null) return;
            _history.Capture("Add MIDI effect");
            Track.MidiEffects.Add(_registry.Create(id));
            Track.CommitMidiEffects();
            Rebuild();
            _events.Publish(new TrackChangedEvent(Track));
        }

        private void Rebuild()
        {
            Effects.Clear();
            if (Track is null) return;
            foreach (var fx in Track.MidiEffects)
                Effects.Add(new MidiEffectSlotViewModel(fx, RemoveEffect, ToggleEffect, Commit));
        }

        private void Commit()
        {
            if (Track is null) return;
            Track.CommitMidiEffects();
            _events.Publish(new TrackChangedEvent(Track));
        }

        private void RemoveEffect(MidiEffectSlotViewModel slot)
        {
            if (Track is null) return;
            _history.Capture("Remove MIDI effect");
            Track.MidiEffects.Remove(slot.Effect);
            Track.CommitMidiEffects();
            Rebuild();
            _events.Publish(new TrackChangedEvent(Track));
        }

        private void ToggleEffect(MidiEffectSlotViewModel slot)
        {
            if (Track is null) return;
            slot.Effect.Enabled = !slot.Effect.Enabled;
            Track.CommitMidiEffects();
            slot.RaiseStatusChanged();
            _events.Publish(new TrackChangedEvent(Track));
        }

        private void OnSelectionChanged()
        {
            OnPropertyChanged(nameof(HasTrack));
            OnPropertyChanged(nameof(TrackName));
            Rebuild();
        }
    }

    public sealed class AvailableMidiEffectViewModel
    {
        private readonly Action<string> _add;
        public AvailableMidiEffectViewModel(MidiEffectInfo info, Action<string> add)
        {
            Info = info;
            _add = add;
            AddCommand = new RelayCommand(() => _add(info.Id));
        }

        public MidiEffectInfo Info { get; }
        public string DisplayName => Info.DisplayName;
        public string Category => Info.Category;
        public RelayCommand AddCommand { get; }
    }

    public sealed class MidiEffectSlotViewModel : ViewModelBase
    {
        private readonly Action<MidiEffectSlotViewModel> _remove;
        private readonly Action<MidiEffectSlotViewModel> _toggle;
        private readonly Action? _commit;

        public MidiEffectSlotViewModel(IMidiEffect effect,
            Action<MidiEffectSlotViewModel> remove,
            Action<MidiEffectSlotViewModel> toggle,
            Action? commit = null)
        {
            Effect = effect;
            _remove = remove;
            _toggle = toggle;
            _commit = commit;
            RemoveCommand = new RelayCommand(() => _remove(this));
            ToggleCommand = new RelayCommand(() => _toggle(this));

            var parameters = new List<ParameterViewModel>();
            foreach (var p in effect.Parameters)
            {
                var vm = ParameterViewModel.Create(p);
                if (vm is FloatParameterViewModel fp && commit is not null)
                    WrapFloatCommit(fp);
                else if (vm is BoolParameterViewModel bp && commit is not null)
                    WrapBoolCommit(bp);
                else if (vm is ChoiceParameterViewModel cp && commit is not null)
                    WrapChoiceCommit(cp);
                parameters.Add(vm);
            }
            Parameters = parameters;
        }

        public IMidiEffect Effect { get; }
        public string Name => Effect.Name;
        public string Status => Effect.Enabled ? "On" : "Off";
        public bool HasParameters => Parameters.Count > 0;
        public IReadOnlyList<ParameterViewModel> Parameters { get; }
        public RelayCommand RemoveCommand { get; }
        public RelayCommand ToggleCommand { get; }
        public void RaiseStatusChanged() => OnPropertyChanged(nameof(Status));

        private void WrapFloatCommit(FloatParameterViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FloatParameterViewModel.Value))
                    _commit?.Invoke();
            };
        }

        private void WrapBoolCommit(BoolParameterViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BoolParameterViewModel.Value))
                    _commit?.Invoke();
            };
        }

        private void WrapChoiceCommit(ChoiceParameterViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChoiceParameterViewModel.SelectedIndex))
                    _commit?.Invoke();
            };
        }
    }
}
