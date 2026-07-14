using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Ongenet.App.Services;

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

    private async void OpenContentAttribution_Click(object? sender, RoutedEventArgs e)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null) return;

        var factory = AppPaths.FactoryContentDirectory();
        var path = factory is not null ? Path.Combine(factory, "ATTRIBUTION.md") : null;
        if (path is not null && File.Exists(path))
        {
            await launcher.LaunchUriAsync(new Uri(Path.GetFullPath(path)));
            return;
        }

        // Fallback: website guide that points users at attribution / pack licences.
        await launcher.LaunchUriAsync(new Uri("https://onge.net/articles/guides/samples-and-libraries.html"));
    }
}
