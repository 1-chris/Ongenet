using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Ongenet.App.Views.Windows;

/// <summary>
/// Standalone fatal-error window (no owner required). Shows a full .NET exception dump and the
/// path to the local crash log. Uses fixed colours so it remains readable even if theming never
/// initialized (startup failures).
/// </summary>
public sealed class CrashDialog : Window
{
    // Catppuccin Mocha — independent of ThemePalette (may be uninitialized during fatal startup).
    private static readonly Color Bg = Color.Parse("#1e1e2e");
    private static readonly Color Fg = Color.Parse("#cdd6f4");
    private static readonly Color Muted = Color.Parse("#a6adc8");

    private CrashDialog(string dump, string? logPath)
    {
        Title = "Ongenet has stopped";
        Width = 720;
        Height = 480;
        MinWidth = 480;
        MinHeight = 320;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Bg);

        var heading = new TextBlock
        {
            Text = "Ongenet has stopped",
            FontWeight = FontWeight.SemiBold,
            FontSize = 16,
            Foreground = new SolidColorBrush(Fg)
        };

        var intro = new TextBlock
        {
            Text = logPath is null
                ? "An unhandled error occurred. A crash log could not be written."
                : "An unhandled error occurred. A local crash log was written (nothing is uploaded):",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Muted)
        };

        var header = new StackPanel { Spacing = 10, Children = { heading, intro } };
        if (logPath is not null)
        {
            header.Children.Add(new SelectableTextBlock
            {
                Text = logPath,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas,Menlo,monospace"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Fg)
            });
        }

        var dumpBox = new TextBox
        {
            Text = dump,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas,Menlo,monospace"),
            FontSize = 12,
            MinHeight = 200,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var copy = new Button { Content = "Copy", Padding = new Thickness(14, 5) };
        copy.Click += async (_, _) =>
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null)
                    await clipboard.SetTextAsync(dump);
            }
            catch
            {
                // Best-effort during a crash.
            }
        };
        buttons.Children.Add(copy);

        if (logPath is not null)
        {
            var openFolder = new Button { Content = "Open log folder", Padding = new Thickness(14, 5) };
            openFolder.Click += (_, _) => TryOpenDirectory(Path.GetDirectoryName(logPath));
            buttons.Children.Add(openFolder);
        }

        var close = new Button { Content = "Close", Padding = new Thickness(14, 5) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        var root = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(buttons);
        root.Children.Add(dumpBox);
        Content = root;
    }

    private static void TryOpenDirectory(string? dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch
        {
            // Best-effort during a crash.
        }
    }

    /// <summary>
    /// Shows the crash dialog and blocks until it is closed. Safe to call from the UI thread
    /// (nested dispatcher frame) or a background thread (marshals to the UI thread).
    /// </summary>
    public static void ShowBlocking(string dump, string? logPath)
    {
        void ShowOnUi()
        {
            var dialog = new CrashDialog(dump, logPath);
            var frame = new DispatcherFrame();
            dialog.Closed += (_, _) => frame.Continue = false;
            dialog.Show();
            Dispatcher.UIThread.PushFrame(frame);
        }

        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                ShowOnUi();
            else
                Dispatcher.UIThread.Invoke(ShowOnUi);
        }
        catch
        {
            // Avalonia may already be torn down; the log file is the fallback.
        }
    }
}
