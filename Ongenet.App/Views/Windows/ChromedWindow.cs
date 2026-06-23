using System;
using Avalonia;
using Avalonia.Controls;
using Ongenet.App.Platform;

namespace Ongenet.App.Views.Windows;

/// <summary>
/// Shared custom-chrome behaviour for the app's borderless windows.
///
/// On Windows/Linux the window is fully self-drawn (no system decorations): the rounded
/// <c>RootBorder</c> is squared off while maximised, and the right-side Windows/Linux button group
/// is used.
///
/// On macOS we instead hand the frame back to the OS (<c>WindowDecorations.Full</c> +
/// <c>ExtendClientAreaToDecorationsHint</c>): macOS draws the real traffic lights over our extended
/// client area, and — crucially — the green button performs a true native fullscreen (its own Space,
/// the slide animation, the auto-hiding menu bar) instead of merely filling the desktop. The custom
/// blob group is reduced to a fixed-width spacer so the title content clears the real lights, and the
/// inner border stays square/borderless because the native frame already supplies corners + shadow.
///
/// Windows opt in purely by naming controls in their XAML (no per-window code needed):
///   • the outer rounded <c>Border</c> as <c>RootBorder</c>;
///   • optionally the left-side macOS blob group as <c>MacWindowButtons</c>;
///   • optionally the right-side Windows/Linux group as <c>StandardWindowButtons</c>;
///   • optionally the custom resize-handle overlay as <c>ResizeHandles</c>.
/// </summary>
public abstract class ChromedWindow : Window
{
    private static bool UseMacChrome => OperatingSystem.IsMacOS(); // || OperatingSystem.IsLinux();

    /// <summary>
    /// Width reserved at the left of the title bar for the native macOS traffic lights. Combined with
    /// the <c>MacWindowButtons</c> panel's own left margin this clears the real (close/min/zoom) blobs.
    /// </summary>
    private const double MacTrafficLightInset = 64;

    private Border? _rootBorder;

    protected ChromedWindow()
    {
        // Decide the frame style *before* the XAML loads (this ctor runs before the derived window's
        // InitializeComponent), so the window is never the wrong kind even briefly.
        //
        // macOS: keep the OS-drawn frame the window is created with (WindowDecorations.Full) — that's
        // what gives the real traffic lights AND native fullscreen via the green button. We must NOT
        // route it through None: toggling the NSWindow style mask to borderless drops the standard
        // window buttons, and switching back to Full does not reliably restore them. The XAML no
        // longer sets SystemDecorations, so the macOS default sticks.
        //
        // Windows/Linux: go borderless and draw our own chrome, as before.
        if (!UseMacChrome)
            WindowDecorations = WindowDecorations.None;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _rootBorder = this.FindControl<Border>("RootBorder");

        if (this.FindControl<Control>("MacWindowButtons") is { } mac)
        {
            if (UseMacChrome)
            {
                // macOS draws the real traffic lights; keep this panel only as a fixed-width spacer
                // so the title content starts to the right of them.
                mac.IsVisible = true;
                if (mac is Panel macPanel)
                    foreach (var child in macPanel.Children)
                        child.IsVisible = false;
                mac.Width = MacTrafficLightInset;
            }
            else
            {
                mac.IsVisible = false;
            }
        }

        // On macOS, keep the native traffic lights visible in fullscreen (they otherwise auto-hide).
        if (UseMacChrome)
            MacTitleBar.SetFullScreen(this, WindowState == WindowState.FullScreen);

        if (this.FindControl<Control>("StandardWindowButtons") is { } standard)
            standard.IsVisible = !UseMacChrome;

        // The native frame handles edge/corner resize on macOS, so the custom overlay handles
        // (which would otherwise sit on top and swallow the native resize cursors) are dropped.
        if (this.FindControl<Control>("ResizeHandles") is { } resize)
            resize.IsVisible = !UseMacChrome;

        UpdateMaximizedChrome();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizedChrome();
            if (UseMacChrome)
            {
                var fullScreen = WindowState == WindowState.FullScreen;
                MacTitleBar.SetFullScreen(this, fullScreen);
                // The state change fires as the fullscreen animation *starts*, so the pin above runs
                // against a mid-transition window. Re-assert at a few staggered points so the final
                // placement is always computed on a fully settled window, whatever its duration —
                // RefreshButtons is idempotent and no-ops if we're no longer fullscreen.
                if (fullScreen)
                    foreach (var seconds in new[] { 0.3, 0.8, 1.5 })
                        Avalonia.Threading.DispatcherTimer.RunOnce(
                            () => MacTitleBar.RefreshButtons(this), TimeSpan.FromSeconds(seconds));
            }
        }
    }

    private void UpdateMaximizedChrome()
    {
        if (_rootBorder is null) return;

        // On macOS the native frame supplies the rounded corners, border and shadow, so the inner
        // border must stay square and borderless to avoid a doubled-up edge.
        if (UseMacChrome)
        {
            _rootBorder.CornerRadius = new CornerRadius(0);
            _rootBorder.BorderThickness = new Thickness(0);
            return;
        }

        // Elsewhere, square off the radius while maximised — it would otherwise expose transparent
        // gaps against the screen edges.
        _rootBorder.CornerRadius = WindowState == WindowState.Maximized
            ? new CornerRadius(0)
            : new CornerRadius(8);
    }
}
