using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ongenet.Desktop.Views.Windows
{
    /// <summary>
    /// A small modal confirm/notify dialog built in code (the app uses custom chrome, so a lightweight
    /// self-contained window avoids extra XAML). Returns true when the confirm button is chosen.
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
            Background = new SolidColorBrush(Color.Parse("#1e1e2e"));

            var heading = new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.Parse("#cdd6f4"))
            };

            var body = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#bac2de"))
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
                cancel.Click += (_, _) => { _result = false; Close(); };
                buttons.Children.Add(cancel);
            }

            var confirm = new Button { Content = confirmText, Padding = new Thickness(14, 5) };
            confirm.Click += (_, _) => { _result = true; Close(); };
            buttons.Children.Add(confirm);

            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children = { heading, body, buttons }
            };
        }

        /// <summary>Shows a Yes/No-style confirmation; resolves true when the confirm button is clicked.</summary>
        public static async Task<bool> Confirm(Window owner, string title, string message,
            string confirmText = "OK", string cancelText = "Cancel")
        {
            var dialog = new MessageDialog(title, message, confirmText, cancelText);
            await dialog.ShowDialog(owner);
            return dialog._result;
        }

        /// <summary>Shows a single-button notification.</summary>
        public static Task Notify(Window owner, string title, string message)
        {
            var dialog = new MessageDialog(title, message, "OK", null);
            return dialog.ShowDialog(owner);
        }
    }
}
