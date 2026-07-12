using System;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels.Panels
{
    /// <summary>MIDI FX tab: per-track MIDI effect chain editor.</summary>
    public class MidiFxViewModel : ViewModelBase
    {
        private readonly ISelectionService _selection;
        private readonly IEventAggregator _events;
        private readonly IHistoryService _history;

        public MidiFxViewModel(ISelectionService selection, IEventAggregator events, IHistoryService history)
        {
            _selection = selection;
            _events = events;
            _history = history;
            _selection.SelectionChanged += OnSelectionChanged;

            AddScaleCommand = new RelayCommand(() => AddEffect(new ScaleMidiEffect()), () => HasTrack);
            AddChordCommand = new RelayCommand(() => AddEffect(new ChordMidiEffect()), () => HasTrack);
            AddArpCommand = new RelayCommand(() => AddEffect(new ArpMidiEffect()), () => HasTrack);
            AddEchoCommand = new RelayCommand(() => AddEffect(new NoteEchoMidiEffect()), () => HasTrack);
            AddRandomCommand = new RelayCommand(() => AddEffect(new RandomMidiEffect()), () => HasTrack);
        }

        public RelayCommand AddScaleCommand { get; }
        public RelayCommand AddChordCommand { get; }
        public RelayCommand AddArpCommand { get; }
        public RelayCommand AddEchoCommand { get; }
        public RelayCommand AddRandomCommand { get; }

        private Track? Track => _selection.SelectedTrack;

        public bool HasTrack => Track is { IsBus: false };

        public string TrackName => Track?.Name ?? string.Empty;

        public ObservableCollection<MidiEffectSlotViewModel> Effects { get; } = new();

        private void AddEffect(IMidiEffect effect)
        {
            if (Track is null) return;
            _history.Capture("Add MIDI effect");
            Track.MidiEffects.Add(effect);
            Track.CommitMidiEffects();
            Rebuild();
            _events.Publish(new TrackChangedEvent(Track));
        }

        private void Rebuild()
        {
            Effects.Clear();
            if (Track is null) return;
            foreach (var fx in Track.MidiEffects)
                Effects.Add(CreateSlot(fx));
        }

        private MidiEffectSlotViewModel CreateSlot(IMidiEffect fx) => fx switch
        {
            ScaleMidiEffect scale => new ScaleMidiEffectSlotViewModel(scale, RemoveEffect, ToggleEffect, Commit),
            ChordMidiEffect chord => new ChordMidiEffectSlotViewModel(chord, RemoveEffect, ToggleEffect, Commit),
            ArpMidiEffect arp => new ArpMidiEffectSlotViewModel(arp, RemoveEffect, ToggleEffect, Commit),
            NoteEchoMidiEffect echo => new NoteEchoMidiEffectSlotViewModel(echo, RemoveEffect, ToggleEffect, Commit),
            RandomMidiEffect random => new RandomMidiEffectSlotViewModel(random, RemoveEffect, ToggleEffect, Commit),
            _ => new MidiEffectSlotViewModel(fx, RemoveEffect, ToggleEffect)
        };

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
            slot.Refresh();
            _events.Publish(new TrackChangedEvent(Track));
        }

        private void OnSelectionChanged()
        {
            Rebuild();
            OnPropertyChanged(nameof(HasTrack));
            OnPropertyChanged(nameof(TrackName));
            AddScaleCommand.RaiseCanExecuteChanged();
            AddChordCommand.RaiseCanExecuteChanged();
            AddArpCommand.RaiseCanExecuteChanged();
            AddEchoCommand.RaiseCanExecuteChanged();
            AddRandomCommand.RaiseCanExecuteChanged();
        }
    }

    public class MidiEffectSlotViewModel : ViewModelBase
    {
        private readonly Action<MidiEffectSlotViewModel> _remove;
        private readonly Action<MidiEffectSlotViewModel> _toggle;

        public MidiEffectSlotViewModel(IMidiEffect effect,
            Action<MidiEffectSlotViewModel> remove,
            Action<MidiEffectSlotViewModel> toggle)
        {
            Effect = effect;
            _remove = remove;
            _toggle = toggle;
            RemoveCommand = new RelayCommand(() => _remove(this));
            ToggleCommand = new RelayCommand(() => _toggle(this));
        }

        public IMidiEffect Effect { get; }

        public RelayCommand RemoveCommand { get; }
        public RelayCommand ToggleCommand { get; }

        public string Name => Effect.Name;
        public bool Enabled => Effect.Enabled;
        public string Status => Enabled ? "On" : "Off";

        public virtual void Refresh()
        {
            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(Status));
        }
    }

    public sealed class ScaleMidiEffectSlotViewModel : MidiEffectSlotViewModel
    {
        private readonly ScaleMidiEffect _fx;
        private readonly Action _commit;

        public ScaleMidiEffectSlotViewModel(ScaleMidiEffect fx,
            Action<MidiEffectSlotViewModel> remove, Action<MidiEffectSlotViewModel> toggle, Action commit)
            : base(fx, remove, toggle)
        {
            _fx = fx;
            _commit = commit;
        }

        public int Root
        {
            get => _fx.Root;
            set { if (_fx.Root == value) return; _fx.Root = value; OnPropertyChanged(); _commit(); }
        }

        public bool Minor
        {
            get => _fx.Minor;
            set { if (_fx.Minor == value) return; _fx.Minor = value; OnPropertyChanged(); _commit(); }
        }
    }

    public sealed class ChordMidiEffectSlotViewModel : MidiEffectSlotViewModel
    {
        private readonly ChordMidiEffect _fx;
        private readonly Action _commit;

        public ChordMidiEffectSlotViewModel(ChordMidiEffect fx,
            Action<MidiEffectSlotViewModel> remove, Action<MidiEffectSlotViewModel> toggle, Action commit)
            : base(fx, remove, toggle)
        {
            _fx = fx;
            _commit = commit;
        }

        public string IntervalsText
        {
            get => string.Join(",", _fx.Intervals);
            set
            {
                var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var ints = parts.Select(p => int.TryParse(p, out var n) ? n : 0).Where(n => n != 0 || parts.Length == 1).ToArray();
                if (ints.Length == 0) ints = new[] { 0, 4, 7 };
                _fx.Intervals = ints;
                OnPropertyChanged();
                _commit();
            }
        }
    }

    public sealed class ArpMidiEffectSlotViewModel : MidiEffectSlotViewModel
    {
        private readonly ArpMidiEffect _fx;
        private readonly Action _commit;

        public ArpMidiEffectSlotViewModel(ArpMidiEffect fx,
            Action<MidiEffectSlotViewModel> remove, Action<MidiEffectSlotViewModel> toggle, Action commit)
            : base(fx, remove, toggle)
        {
            _fx = fx;
            _commit = commit;
        }

        public double RateBeats
        {
            get => _fx.RateBeats;
            set { if (Math.Abs(_fx.RateBeats - value) < 1e-9) return; _fx.RateBeats = value; OnPropertyChanged(); _commit(); }
        }
    }

    public sealed class NoteEchoMidiEffectSlotViewModel : MidiEffectSlotViewModel
    {
        private readonly NoteEchoMidiEffect _fx;
        private readonly Action _commit;

        public NoteEchoMidiEffectSlotViewModel(NoteEchoMidiEffect fx,
            Action<MidiEffectSlotViewModel> remove, Action<MidiEffectSlotViewModel> toggle, Action commit)
            : base(fx, remove, toggle)
        {
            _fx = fx;
            _commit = commit;
        }

        public double DelayBeats
        {
            get => _fx.DelayBeats;
            set { if (Math.Abs(_fx.DelayBeats - value) < 1e-9) return; _fx.DelayBeats = value; OnPropertyChanged(); _commit(); }
        }

        public float Feedback
        {
            get => _fx.Feedback;
            set { if (Math.Abs(_fx.Feedback - value) < 1e-6f) return; _fx.Feedback = value; OnPropertyChanged(); _commit(); }
        }

        public int MaxEchoes
        {
            get => _fx.MaxEchoes;
            set { if (_fx.MaxEchoes == value) return; _fx.MaxEchoes = value; OnPropertyChanged(); _commit(); }
        }
    }

    public sealed class RandomMidiEffectSlotViewModel : MidiEffectSlotViewModel
    {
        private readonly RandomMidiEffect _fx;
        private readonly Action _commit;

        public RandomMidiEffectSlotViewModel(RandomMidiEffect fx,
            Action<MidiEffectSlotViewModel> remove, Action<MidiEffectSlotViewModel> toggle, Action commit)
            : base(fx, remove, toggle)
        {
            _fx = fx;
            _commit = commit;
        }

        public float Probability
        {
            get => _fx.Probability;
            set { if (Math.Abs(_fx.Probability - value) < 1e-6f) return; _fx.Probability = value; OnPropertyChanged(); _commit(); }
        }
    }
}
