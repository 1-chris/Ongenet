using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Ongenet.App.Localization;
using Ongenet.App.Theming;

namespace Ongenet.App.Views.Windows
{
    /// <summary>
    /// A small modal confirm/notify dialog built in code (the app uses custom chrome, so a lightweight
    /// self-contained window avoids extra XAML). Returns true when the confirm button is chosen.
    /// On single-view hosts (browser / Android) falls back to an <see cref="OverlayLayer"/> panel.
    /// </summary>
    public sealed class MessageDialog : Window
    {
        private bool _result;

        private MessageDialog(string title, string message, string confirmText, string? cancelText)
        {
            Title = title;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(ThemePalette.Base);

            Content = BuildContent(title, message, confirmText, cancelText,
                onConfirm: () => { _result = true; Close(); },
                onCancel: () => { _result = false; Close(); });
        }

        /// <summary>Shows a Yes/No-style confirmation; resolves true when the confirm button is clicked.</summary>
        public static Task<bool> Confirm(Window owner, string title, string message,
            string? confirmText = null, string? cancelText = null)
            => Confirm((Visual)owner, title, message, confirmText, cancelText);

        /// <summary>Shows a Yes/No-style confirmation from any visual (window or single-view shell).</summary>
        public static async Task<bool> Confirm(Visual owner, string title, string message,
            string? confirmText = null, string? cancelText = null)
        {
            confirmText ??= Loc.Get("Dialog_OK");
            cancelText ??= Loc.Get("Dialog_Cancel");

            if (TopLevel.GetTopLevel(owner) is Window window)
            {
                var dialog = new MessageDialog(title, message, confirmText, cancelText);
                await dialog.ShowDialog(window);
                return dialog._result;
            }

            return await ShowOverlayAsync(owner, title, message, confirmText, cancelText);
        }

        /// <summary>Shows a single-button notification.</summary>
        public static Task Notify(Window owner, string title, string message)
            => Notify((Visual)owner, title, message);

        /// <summary>Shows a single-button notification from any visual (window or single-view shell).</summary>
        public static async Task Notify(Visual owner, string title, string message)
        {
            var ok = Loc.Get("Dialog_OK");
            if (TopLevel.GetTopLevel(owner) is Window window)
            {
                var dialog = new MessageDialog(title, message, ok, null);
                await dialog.ShowDialog(window);
                return;
            }

            await ShowOverlayAsync(owner, title, message, ok, cancelText: null);
        }

        private static async Task<bool> ShowOverlayAsync(Visual owner, string title, string message,
            string confirmText, string? cancelText)
        {
            var top = TopLevel.GetTopLevel(owner);
            var layer = top is null ? null : OverlayLayer.GetOverlayLayer(top);
            if (layer is null)
                return true; // no UI host — allow the action rather than trapping the user

            var tcs = new TaskCompletionSource<bool>();
            Control? overlay = null;

            void Finish(bool result)
            {
                if (overlay is not null)
                    layer.Children.Remove(overlay);
                tcs.TrySetResult(result);
            }

            overlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                Child = new Border
                {
                    Background = new SolidColorBrush(ThemePalette.Base),
                    BorderBrush = new SolidColorBrush(ThemePalette.Surface1),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    MaxWidth = 440,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = BuildContent(title, message, confirmText, cancelText,
                        onConfirm: () => Finish(true),
                        onCancel: () => Finish(false))
                }
            };

            layer.Children.Add(overlay);
            return await tcs.Task;
        }

        private static Control BuildContent(string title, string message, string confirmText, string? cancelText,
            System.Action onConfirm, System.Action onCancel)
        {
            var heading = new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                Foreground = new SolidColorBrush(ThemePalette.Text)
            };

            var body = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(ThemePalette.Subtext1)
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            if (cancelText is not null)
            {
                var cancel = new Button { Content = cancelText, Padding = new Thickness(14, 5) };
                cancel.Click += (_, _) => onCancel();
                buttons.Children.Add(cancel);
            }

            var confirm = new Button { Content = confirmText, Padding = new Thickness(14, 5) };
            confirm.Click += (_, _) => onConfirm();
            buttons.Children.Add(confirm);

            return new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children = { heading, body, buttons }
            };
        }
    }
}
