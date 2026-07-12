using Avalonia.Controls;

namespace Ongenet.App.Views.Windows;

public partial class AudioEditorWindow : Window
{
    public AudioEditorWindow()
    {
        InitializeComponent();
        Closing += (_, _) =>
        {
            if (DataContext is ViewModels.AudioEditorViewModel vm)
                vm.CloseAll();
        };
    }
}
