using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Desktop.Services;

namespace Ongenet.Desktop.ViewModels.Effects
{
    /// <summary>One effect in a track's chain: its name, parameters, and a remove command.</summary>
    public class EffectViewModel : ViewModelBase
    {
        private int _position;
        private bool _isFirst;
        private bool _isLast;

        public EffectViewModel(IAudioEffect effect, Action<EffectViewModel> remove,
            Action<EffectViewModel> moveUp, Action<EffectViewModel> moveDown)
        {
            Effect = effect;
            var parameters = new List<ParameterViewModel>();
            foreach (var p in effect.Parameters) parameters.Add(ParameterViewModel.Create(p));
            Parameters = parameters;
            RemoveCommand = new RelayCommand(() => remove(this));
            ToggleEnabledCommand = new RelayCommand(() => IsEnabled = !IsEnabled);
            MoveUpCommand = new RelayCommand(() => moveUp(this));
            MoveDownCommand = new RelayCommand(() => moveDown(this));
        }

        public IAudioEffect Effect { get; }
        public string Name => Effect.Name;
        public IReadOnlyList<ParameterViewModel> Parameters { get; }
        public RelayCommand RemoveCommand { get; }

        private string _presetName = string.Empty;

        /// <summary>The name typed into the "Save preset" flyout.</summary>
        public string PresetName
        {
            get => _presetName;
            set => SetField(ref _presetName, value);
        }

        /// <summary>Saves this effect's current settings as a user <c>.ongenpreset</c>.</summary>
        public void SaveAsPreset()
        {
            var name = string.IsNullOrWhiteSpace(_presetName) ? Name : _presetName.Trim();
            App.ServiceProvider?.GetService<IPresetLibrary>()?.SaveEffect(Effect, name);
            PresetName = string.Empty;
        }

        /// <summary>Re-reads the enabled state and parameters (so automation shows live during playback).</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(IsEnabled));
            foreach (var p in Parameters) p.Refresh();
        }

        /// <summary>Toggles whether the effect processes audio (the green/red dot).</summary>
        public RelayCommand ToggleEnabledCommand { get; }

        /// <summary>Moves this effect earlier/later in the processing chain.</summary>
        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }

        /// <summary>1-based position in the chain (1 = processed first).</summary>
        public int Position
        {
            get => _position;
            set => SetField(ref _position, value);
        }

        /// <summary>True for the first/last effect in the chain (disables the up/down buttons).</summary>
        public bool IsFirst
        {
            get => _isFirst;
            set => SetField(ref _isFirst, value);
        }

        public bool IsLast
        {
            get => _isLast;
            set => SetField(ref _isLast, value);
        }

        /// <summary>Whether the effect is active; when false the engine bypasses it.</summary>
        public bool IsEnabled
        {
            get => Effect.Enabled;
            set
            {
                if (Effect.Enabled == value) return;
                App.ServiceProvider?.GetService<IHistoryService>()?.Capture(value ? "Enable effect" : "Bypass effect");
                Effect.Enabled = value;
                OnPropertyChanged();
            }
        }

        // --- Plugin editor (CLAP effect GUI) ---

        /// <summary>The effect's GUI editor, if it has one (a CLAP plugin); else null.</summary>
        public IPluginEditor? Editor => Effect as IPluginEditor;

        public bool HasEditor => Editor is { HasEditor: true };
        public bool IsEditorOpen => Editor is { IsEditorOpen: true };
        public string EditorButtonText => IsEditorOpen ? "Close plugin UI" : "Open plugin UI";

        /// <summary>Re-reads the editor open state (button text) after the plugin window changes.</summary>
        public void NotifyEditorState()
        {
            OnPropertyChanged(nameof(IsEditorOpen));
            OnPropertyChanged(nameof(EditorButtonText));
        }
    }
}
