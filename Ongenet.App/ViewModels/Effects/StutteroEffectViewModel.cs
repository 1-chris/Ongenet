using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Modulation;
using Ongenet.Core.Audio.Modules;

namespace Ongenet.App.ViewModels.Effects
{
    /// <summary>
    /// A <see cref="StutteroEffect"/> in the chain. Surfaces its gestures (each with stutter/buffer/gate
    /// settings and a trigger key), a single curve editor driven by a target selector (gate / rate /
    /// cutoff / per-module depth), and the reorderable FX module rack. The generic Mode/Mix/Freeze/Output
    /// knobs come from the base <see cref="EffectViewModel.Parameters"/>.
    /// </summary>
    public sealed class StutteroEffectViewModel : EffectViewModel
    {
        private readonly StutteroEffect _fx;
        private GestureItemViewModel? _selectedGesture;
        private CurveTargetViewModel? _selectedTarget;
        private ModulationCurve? _editCurve;
        private int _curveRevision;
        private int _selectedPreset;

        public StutteroEffectViewModel(StutteroEffect effect, Action<EffectViewModel> remove,
            Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
            : base(effect, remove, moveUp, moveDown)
        {
            _fx = effect;
            RebuildGestures();
            RebuildModules();

            AddGestureCommand = new RelayCommand(AddGesture);
            RemoveGestureCommand = new RelayCommand(RemoveSelectedGesture);
            ApplyPresetCommand = new RelayCommand(ApplyPreset);
            ClearCurveCommand = new RelayCommand(ClearCurve);

            SelectedGesture = Gestures.FirstOrDefault();
        }

        // --- Gestures ---------------------------------------------------------------------------

        public ObservableCollection<GestureItemViewModel> Gestures { get; } = new();

        public GestureItemViewModel? SelectedGesture
        {
            get => _selectedGesture;
            set
            {
                if (!SetField(ref _selectedGesture, value)) return;
                RebuildCurveTargets();
                OnPropertyChanged(nameof(HasSelectedGesture));
            }
        }

        public bool HasSelectedGesture => _selectedGesture is not null;

        public RelayCommand AddGestureCommand { get; }
        public RelayCommand RemoveGestureCommand { get; }

        // --- Auto-mode gesture selection --------------------------------------------------------

        public IReadOnlyList<string> AutoGestureOptions => Gestures.Select(g => g.Name).ToList();

        public int AutoGestureIndex
        {
            get => _fx.AutoGestureIndex;
            set { _fx.AutoGestureIndex = value; OnPropertyChanged(); }
        }

        // --- Curve editor (driven by a target selector) -----------------------------------------

        public ObservableCollection<CurveTargetViewModel> CurveTargets { get; } = new();

        public CurveTargetViewModel? SelectedTarget
        {
            get => _selectedTarget;
            set
            {
                if (!SetField(ref _selectedTarget, value)) return;
                EditCurve = value?.Resolve();
                OnPropertyChanged(nameof(CanClearCurve));
            }
        }

        public ModulationCurve? EditCurve
        {
            get => _editCurve;
            private set
            {
                if (!SetField(ref _editCurve, value)) return;
                OnPropertyChanged(nameof(Palindrome));
                OnPropertyChanged(nameof(QuantizeSteps));
                BumpCurve();
            }
        }

        /// <summary>Forces the curve control to repaint after the bound curve mutates in place.</summary>
        public int CurveRevision
        {
            get => _curveRevision;
            private set => SetField(ref _curveRevision, value);
        }

        private void BumpCurve() => CurveRevision++;

        public bool Palindrome
        {
            get => _editCurve?.Palindrome ?? false;
            set { if (_editCurve is null) return; _editCurve.Palindrome = value; OnPropertyChanged(); }
        }

        public int QuantizeSteps
        {
            get => _editCurve?.QuantizeSteps ?? 0;
            set { if (_editCurve is null) return; _editCurve.QuantizeSteps = Math.Max(0, value); OnPropertyChanged(); }
        }

        public IReadOnlyList<string> CurvePresets { get; } = CurveShapes.All.Select(s => s.Name).ToList();

        public int SelectedPreset
        {
            get => _selectedPreset;
            set => SetField(ref _selectedPreset, value);
        }

        public RelayCommand ApplyPresetCommand { get; }
        public RelayCommand ClearCurveCommand { get; }
        public bool CanClearCurve => _selectedTarget?.Clear is not null;

        private void ApplyPreset()
        {
            if (_editCurve is null) return;
            CurveShapes.Apply(_editCurve, _selectedPreset);
            BumpCurve();
        }

        private void ClearCurve()
        {
            _selectedTarget?.Clear?.Invoke();
            // Re-resolve so the editor shows the (recreated/empty) curve and the engine drops the assignment.
            RebuildCurveTargets();
        }

        // --- Module rack ------------------------------------------------------------------------

        public ObservableCollection<ModuleItemViewModel> Modules { get; } = new();

        private void RebuildGestures()
        {
            Gestures.Clear();
            foreach (var g in _fx.Gestures) Gestures.Add(new GestureItemViewModel(_fx, g));
            OnPropertyChanged(nameof(AutoGestureOptions));
        }

        private void RebuildModules()
        {
            Modules.Clear();
            var list = _fx.Rack.Modules;
            for (var i = 0; i < list.Count; i++)
            {
                var item = new ModuleItemViewModel(list[i], MoveModuleUp, MoveModuleDown)
                {
                    IsFirst = i == 0,
                    IsLast = i == list.Count - 1
                };
                Modules.Add(item);
            }
        }

        private void MoveModuleUp(ModuleItemViewModel item) => MoveModule(item, -1);
        private void MoveModuleDown(ModuleItemViewModel item) => MoveModule(item, +1);

        private void MoveModule(ModuleItemViewModel item, int delta)
        {
            var from = _fx.Rack.Modules.IndexOf(item.Module);
            if (from < 0) return;
            _fx.Rack.Move(from, from + delta); // commits the new order to the audio thread
            RebuildModules();
        }

        private void AddGesture()
        {
            var g = new StutterGesture { Name = $"Gesture {_fx.Gestures.Count + 1}" };
            _fx.Gestures.Add(g);
            RebuildGestures();
            SelectedGesture = Gestures.LastOrDefault();
        }

        private void RemoveSelectedGesture()
        {
            if (_selectedGesture is null || _fx.Gestures.Count <= 1) return;
            var index = _fx.Gestures.IndexOf(_selectedGesture.Gesture);
            if (index < 0) return;

            // Keep the key map valid: drop this gesture's keys and shift higher indices down.
            for (var n = 0; n < _fx.KeyMap.Length; n++)
            {
                if (_fx.KeyMap[n] == index) _fx.KeyMap[n] = -1;
                else if (_fx.KeyMap[n] > index) _fx.KeyMap[n]--;
            }

            _fx.Gestures.RemoveAt(index);
            if (_fx.AutoGestureIndex >= _fx.Gestures.Count) _fx.AutoGestureIndex = _fx.Gestures.Count - 1;
            RebuildGestures();
            SelectedGesture = Gestures.FirstOrDefault();
            OnPropertyChanged(nameof(AutoGestureIndex));
        }

        private void RebuildCurveTargets()
        {
            CurveTargets.Clear();
            if (_selectedGesture is { } gvm)
            {
                var g = gvm.Gesture;
                CurveTargets.Add(new CurveTargetViewModel("Gate (per-slice)", () => g.Gate, null));
                CurveTargets.Add(new CurveTargetViewModel("Stutter Rate",
                    () => g.Rate ??= NewRamp(), () => g.Rate = null));
                CurveTargets.Add(new CurveTargetViewModel("Filter Cutoff",
                    () => { EnableModule(LowPassModule.ModuleId); return g.Cutoff ??= NewRamp(); },
                    () => g.Cutoff = null));

                foreach (var m in _fx.Rack.Modules)
                {
                    var id = m.Id;
                    var name = m.Name;
                    CurveTargets.Add(new CurveTargetViewModel($"{name} depth",
                        () => { EnableModule(id); return Get(g.ModuleCurves, id); },
                        () => g.ModuleCurves.Remove(id)));
                }
            }

            SelectedTarget = CurveTargets.FirstOrDefault();
        }

        private void EnableModule(string id)
        {
            var item = Modules.FirstOrDefault(m => m.Module.Id == id);
            if (item is { IsEnabled: false }) item.IsEnabled = true;
        }

        private static ModulationCurve Get(Dictionary<string, ModulationCurve> map, string id)
        {
            if (!map.TryGetValue(id, out var c)) { c = new ModulationCurve(new[] { new Core.Audio.Automation.AutomationPoint(0, 1) }); map[id] = c; }
            return c;
        }

        private static ModulationCurve NewRamp()
            => new(new[] { new Core.Audio.Automation.AutomationPoint(0, 0), new Core.Audio.Automation.AutomationPoint(1, 1) });
    }
}
