using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Views.Windows;

/// <summary>Built-in help — localized topics with optional external resource links.</summary>
public partial class GuideWindow : ChromedWindow
{
    public GuideWindow()
    {
        InitializeComponent();
        DataContext = App.ServiceProvider?.GetRequiredService<GuideViewModel>()
            ?? throw new InvalidOperationException("GuideViewModel is not registered.");
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control c || c is Button || c.Parent is Button) return;
        BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private async void OpenLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null)
            await launcher.LaunchUriAsync(new Uri(url));
    }
}
