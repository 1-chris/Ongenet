using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ongenet.App.Theming;

namespace Ongenet.App.Views.Windows
{
    /// <summary>
    /// A small modal single-line text prompt (the companion to <see cref="MessageDialog"/>), used for
    /// rename actions launched from context menus. Enter confirms, Escape cancels. Returns the entered
    /// text, or null when cancelled (or left empty).
    /// </summary>
    public sealed class InputDialog : Window
    {
        private readonly TextBox _input;
        private string? _result;

        private InputDialog(string title, string label, string initialText, string confirmText)
        {
            Title = title;
            Width = 400;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(ThemePalette.Base);

            var heading = new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                Foreground = new SolidColorBrush(ThemePalette.Text)
            };

            var caption = new TextBlock
            {
                Text = label,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(ThemePalette.Subtext1)
            };

            _input = new TextBox { Text = initialText };
            _input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
                else if (e.Key == Key.Escape) { _result = null; Close(); e.Handled = true; }
            };

            var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 5) };
            cancel.Click += (_, _) => { _result = null; Close(); };

            var confirm = new Button { Content = confirmText, Padding = new Thickness(14, 5) };
            confirm.Click += (_, _) => Accept();

            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children =
                {
                    heading, caption, _input,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, confirm }
                    }
                }
            };

            Opened += (_, _) => { _input.Focus(); _input.SelectAll(); };
        }

        private void Accept()
        {
            var text = _input.Text?.Trim();
            _result = string.IsNullOrEmpty(text) ? null : text;
            Close();
        }

        /// <summary>Shows the prompt; resolves to the entered text, or null when cancelled/empty.</summary>
        public static async Task<string?> Prompt(Window owner, string title, string label,
            string initialText = "", string confirmText = "OK")
        {
            var dialog = new InputDialog(title, label, initialText, confirmText);
            await dialog.ShowDialog(owner);
            return dialog._result;
        }
    }
}
