using Avalonia.Controls;
using Avalonia.Interactivity;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Views.Windows;

public partial class AudioToMidiWindow : Window
{
    public AudioToMidiWindow()
    {
        InitializeComponent();
    }

    public static void Show(Window owner, AudioToMidiViewModel vm)
    {
        var win = new AudioToMidiWindow { DataContext = vm };
        vm.RequestClose += win.Close;
        win.ShowDialog(owner);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
