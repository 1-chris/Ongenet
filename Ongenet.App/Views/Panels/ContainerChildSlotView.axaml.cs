using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ongenet.App.ViewModels.Instruments;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.App.Views.Panels;

public partial class ContainerChildSlotView : UserControl
{
    public ContainerChildSlotView() => InitializeComponent();

    private void OnReplaceInstrument(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ContainerChildSlotViewModel vm) return;
        if (sender is not Button { DataContext: InstrumentInfo info }) return;
        vm.ReplaceCommand.Execute(info.Id);
    }

    private async void OnLoadSample(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ContainerChildSlotViewModel vm) return;
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
}
