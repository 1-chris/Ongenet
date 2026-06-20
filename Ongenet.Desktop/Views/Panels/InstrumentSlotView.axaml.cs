using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ongenet.Desktop.ViewModels.Instruments;

namespace Ongenet.Desktop.Views.Panels
{
    /// <summary>
    /// One instrument card in the rack. The "Load sample/SFZ" buttons open file pickers; "Open plugin UI"
    /// opens a CLAP/LV2 plugin's own GUI window. DataContext is an <see cref="InstrumentSlotViewModel"/>.
    /// </summary>
    public partial class InstrumentSlotView : UserControl
    {
        public InstrumentSlotView()
        {
            InitializeComponent();
        }

        private void OnTogglePluginUi(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not InstrumentSlotViewModel vm || vm.CurrentEditor is null) return;
            Windows.PluginEditorHost.Toggle(vm.CurrentEditor, vm.InstrumentName, TopLevel.GetTopLevel(this) as Window);
        }

        private async void OnLoadSample(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not InstrumentSlotViewModel vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load sample",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Audio") { Patterns = new[] { "*.wav", "*.wave" } }
                }
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) vm.LoadSampleFromPath(path);
        }

        private async void OnLoadSfz(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not InstrumentSlotViewModel vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load SFZ instrument",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("SFZ instrument") { Patterns = new[] { "*.sfz" } }
                }
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) vm.LoadSfzFromPath(path);
        }

        private void OnBendReleased(object? sender, PointerReleasedEventArgs e)
            => (DataContext as InstrumentSlotViewModel)?.ResetPitchBend();

        private void OnBendCaptureLost(object? sender, PointerCaptureLostEventArgs e)
            => (DataContext as InstrumentSlotViewModel)?.ResetPitchBend();
    }
}
