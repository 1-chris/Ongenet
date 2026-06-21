using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Desktop.ViewModels.Effects;
using Ongenet.Desktop.Views.Windows;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// Reusable editor for one insert-effect chain (track-level or per-instrument). Hosts CLAP effect
    /// GUIs in their own windows, and accepts effects / effect presets dragged from the library.
    /// DataContext is an <see cref="EffectChainViewModel"/>.
    /// </summary>
    public partial class EffectChainView : UserControl
    {
        public EffectChainView()
        {
            InitializeComponent();
            PluginEditorHost.EditorStateChanged += OnEditorStateChanged;

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        private static bool Accepts(DragEventArgs e)
            => e.DataTransfer.Contains(DragFormats.Effect)
               || e.DataTransfer.Contains(DragFormats.Preset)
               || e.DataTransfer.Contains(DragFormats.EffectChain);

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = DataContext is EffectChainViewModel && Accepts(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            if (DataContext is not EffectChainViewModel vm) return;
            if (e.DataTransfer.TryGetValue(DragFormats.Effect) is { } id)
            {
                vm.AddEffect(id);
                e.Handled = true;
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.Preset) is { } presetPath)
            {
                vm.AddEffectPreset(presetPath);
                e.Handled = true;
            }
            else if (e.DataTransfer.TryGetValue(DragFormats.EffectChain) is { } chainPath)
            {
                vm.AddEffectChainPreset(chainPath);
                e.Handled = true;
            }
        }

        private void OnSaveChain(object? sender, RoutedEventArgs e)
        {
            (DataContext as EffectChainViewModel)?.SaveChainAsPreset();
            SaveChainButton.Flyout?.Hide();
        }

        private void OnToggleEffectPluginUi(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not EffectViewModel vm || vm.Editor is null) return;
            PluginEditorHost.Toggle(vm.Editor, vm.Name, TopLevel.GetTopLevel(this) as Window);
        }

        // Refresh the matching effect card's open/close button when its editor's state changes.
        private void OnEditorStateChanged(IPluginEditor editor)
        {
            if (DataContext is EffectChainViewModel vm) vm.RefreshEditor(editor);
        }
    }
}
