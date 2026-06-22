using System;
using Avalonia;
using Avalonia.Controls;

namespace Ongenet.App.Views.Windows;

/// <summary>
/// Shared custom-chrome behaviour for the app's borderless windows:
///   • squares off the rounded corners while the window is maximised (the radius would otherwise
///     just expose transparent gaps against the screen edges), and
///   • picks the platform-appropriate window-button layout — macOS gets the left-side "traffic
///     light" blobs, everyone else the right-side buttons.
///
/// Windows opt in purely by naming controls in their XAML (no per-window code needed):
///   • the outer rounded <c>Border</c> as <c>RootBorder</c>;
///   • optionally the left-side macOS blob group as <c>MacWindowButtons</c>;
///   • optionally the right-side Windows/Linux group as <c>StandardWindowButtons</c>.
/// </summary>
public abstract class ChromedWindow : Window
{
    private static bool UseMacChrome => OperatingSystem.IsMacOS(); // || OperatingSystem.IsLinux();

    private Border? _rootBorder;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _rootBorder = this.FindControl<Border>("RootBorder");
        if (this.FindControl<Control>("MacWindowButtons") is { } mac) mac.IsVisible = UseMacChrome;
        if (this.FindControl<Control>("StandardWindowButtons") is { } standard) standard.IsVisible = !UseMacChrome;
        UpdateMaximizedChrome();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty) UpdateMaximizedChrome();
    }

    private void UpdateMaximizedChrome()
    {
        if (_rootBorder is null) return;
        _rootBorder.CornerRadius = WindowState == WindowState.Maximized
            ? new CornerRadius(0)
            : new CornerRadius(8);
    }
}
