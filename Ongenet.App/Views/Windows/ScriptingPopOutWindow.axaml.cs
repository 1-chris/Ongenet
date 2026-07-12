using Avalonia.Controls;
using Avalonia.Input;

namespace Ongenet.App.Views.Windows;

public partial class ScriptingPopOutWindow : ChromedWindow
{
    public ScriptingPopOutWindow() => InitializeComponent();

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control c || c is Button || c.Parent is Button) return;
        BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
