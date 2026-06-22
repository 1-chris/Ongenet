using Avalonia.Controls;
using Avalonia.Interactivity;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Views.Settings;

/// <summary>MIDI input/device, input-quantize, transport mapping and CC-mapping UI for the Settings window.</summary>
public partial class MidiSettingsView : UserControl
{
    public MidiSettingsView()
    {
        InitializeComponent();
    }

    private void RefreshDevices_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MidiSettingsViewModel vm) vm.RefreshDevices();
    }

    private void LearnTransport_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MidiSettingsViewModel vm && (sender as Control)?.DataContext is TransportMapRow row)
            vm.LearnTransport(row.Action);
    }

    private void ClearTransport_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MidiSettingsViewModel vm && (sender as Control)?.DataContext is TransportMapRow row)
            vm.ClearTransport(row.Action);
    }

    private void RemoveMapping_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MidiSettingsViewModel vm && (sender as Control)?.DataContext is MidiMappingRow row)
            vm.RemoveMapping(row);
    }
}
