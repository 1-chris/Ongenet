using Avalonia.Controls;
using Avalonia.Interactivity;
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
}
