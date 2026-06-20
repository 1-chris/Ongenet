using Avalonia.Controls;
using Avalonia.Interactivity;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Desktop.ViewModels.Effects;
using Ongenet.Desktop.Views.Windows;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// Reusable editor for one insert-effect chain (track-level or per-instrument). Hosts CLAP effect
    /// GUIs in their own windows. DataContext is an <see cref="EffectChainViewModel"/>.
    /// </summary>
    public partial class EffectChainView : UserControl
    {
        public EffectChainView()
        {
            InitializeComponent();
            PluginEditorHost.EditorStateChanged += OnEditorStateChanged;
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
