using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Modulation;
using Ongenet.Core.Audio.Modules;

namespace Ongenet.Desktop.ViewModels.Effects
{
    /// <summary>One gesture row in the Stuttero editor: name, subdivision, buffer mode, lengths, tail,
    /// and the MIDI trigger key (stored in the effect's key map). Edits write straight to the live
    /// gesture, which the audio thread reads on its next slice.</summary>
    public sealed class GestureItemViewModel : ViewModelBase
    {
        private readonly StutteroEffect _fx;

        public GestureItemViewModel(StutteroEffect fx, StutterGesture gesture)
        {
            _fx = fx;
            Gesture = gesture;
        }

        public StutterGesture Gesture { get; }

        public string Name
        {
            get => Gesture.Name;
            set { if (Gesture.Name == value) return; Gesture.Name = value; OnPropertyChanged(); }
        }

        public IReadOnlyList<string> RateOptions { get; } = BuildRateNames();

        public int RateIndex
        {
            get => Gesture.RateIndex;
            set { Gesture.RateIndex = value; OnPropertyChanged(); }
        }

        public IReadOnlyList<string> BufferOptions { get; } = new[] { "Lock", "Slide", "Random" };

        public int BufferModeIndex
        {
            get => (int)Gesture.Buffer;
            set { Gesture.Buffer = (BufferMode)value; OnPropertyChanged(); }
        }

        public double LengthBeats
        {
            get => Gesture.LengthBeats;
            set { Gesture.LengthBeats = Math.Max(0.25, value); OnPropertyChanged(); }
        }

        public double BufferLengthBeats
        {
            get => Gesture.BufferLengthBeats;
            set { Gesture.BufferLengthBeats = Math.Max(0.03125, value); OnPropertyChanged(); }
        }

        public double TailMs
        {
            get => Gesture.TailMs;
            set { Gesture.TailMs = Math.Max(0, value); OnPropertyChanged(); }
        }

        /// <summary>MIDI note that triggers this gesture (-1 = unmapped). One key per gesture in v1.</summary>
        public int TriggerKey
        {
            get
            {
                var idx = _fx.Gestures.IndexOf(Gesture);
                for (var n = 0; n < _fx.KeyMap.Length; n++) if (_fx.KeyMap[n] == idx) return n;
                return -1;
            }
            set
            {
                var idx = _fx.Gestures.IndexOf(Gesture);
                for (var n = 0; n < _fx.KeyMap.Length; n++) if (_fx.KeyMap[n] == idx) _fx.KeyMap[n] = -1;
                if (value >= 0 && value < _fx.KeyMap.Length) _fx.KeyMap[value] = idx;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TriggerKeyName));
            }
        }

        public string TriggerKeyName => TriggerKey < 0 ? "—" : NoteName(TriggerKey);

        private static IReadOnlyList<string> BuildRateNames()
        {
            var names = new string[StutterRates.All.Length];
            for (var i = 0; i < names.Length; i++) names[i] = StutterRates.All[i].Name;
            return names;
        }

        private static readonly string[] Pitches = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        private static string NoteName(int note) => $"{Pitches[note % 12]}{note / 12 - 1}";
    }

    /// <summary>One module row in the rack: enable toggle, generic parameters, and reorder buttons.</summary>
    public sealed class ModuleItemViewModel : ViewModelBase
    {
        private bool _isFirst;
        private bool _isLast;

        public ModuleItemViewModel(FxModule module, Action<ModuleItemViewModel> moveUp, Action<ModuleItemViewModel> moveDown)
        {
            Module = module;
            var ps = new List<ParameterViewModel>();
            foreach (var p in module.Parameters) ps.Add(ParameterViewModel.Create(p));
            Parameters = ps;
            MoveUpCommand = new RelayCommand(() => moveUp(this));
            MoveDownCommand = new RelayCommand(() => moveDown(this));
        }

        public FxModule Module { get; }
        public string Name => Module.Name;
        public IReadOnlyList<ParameterViewModel> Parameters { get; }

        public bool IsEnabled
        {
            get => Module.Enabled;
            set { if (Module.Enabled == value) return; Module.Enabled = value; OnPropertyChanged(); }
        }

        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }

        public bool IsFirst { get => _isFirst; set => SetField(ref _isFirst, value); }
        public bool IsLast { get => _isLast; set => SetField(ref _isLast, value); }
    }

    /// <summary>A selectable destination for the shared curve editor (gate / rate / cutoff / a module's
    /// depth). <see cref="Resolve"/> gets-or-creates the backing curve; <see cref="Clear"/> removes an
    /// optional assignment (null for the always-present gate curve).</summary>
    public sealed class CurveTargetViewModel
    {
        public CurveTargetViewModel(string label, Func<ModulationCurve> resolve, Action? clear)
        {
            Label = label;
            Resolve = resolve;
            Clear = clear;
        }

        public string Label { get; }
        public Func<ModulationCurve> Resolve { get; }
        public Action? Clear { get; }

        public override string ToString() => Label;
    }
}
