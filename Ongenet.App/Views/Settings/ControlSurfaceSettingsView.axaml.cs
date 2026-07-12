using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Views.Settings;

public partial class ControlSurfaceSettingsView : UserControl
{
    public ControlSurfaceSettingsView() => InitializeComponent();

    private void LearnMapping_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ControlSurfaceSettingsViewModel vm
            && sender is Button { DataContext: ControlSurfaceMappingRow row })
            vm.LearnMapping(row.MixerChannel, row.Target);
    }

    private void ClearMapping_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ControlSurfaceSettingsViewModel vm
            && sender is Button { DataContext: ControlSurfaceMappingRow row })
            vm.ClearMapping(row.MixerChannel, row.Target);
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ControlSurfaceSettingsViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { } storage) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import control surface mapping",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } }
            }
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        vm.ImportFromFile(path);
    }
}
