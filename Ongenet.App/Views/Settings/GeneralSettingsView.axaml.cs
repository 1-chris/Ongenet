using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ongenet.App.Views.Settings;

public partial class GeneralSettingsView : UserControl
{
    public GeneralSettingsView() => InitializeComponent();

    private async void OpenLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null)
            await launcher.LaunchUriAsync(new Uri(url));
    }
}
