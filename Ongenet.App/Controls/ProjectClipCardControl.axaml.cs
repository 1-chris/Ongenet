using Avalonia.Controls;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Controls;

public partial class ProjectClipCardControl : UserControl
{
    public ProjectClipCardControl()
    {
        InitializeComponent();
    }

    public ProjectClipItemViewModel? Item
    {
        get => DataContext as ProjectClipItemViewModel;
        set => DataContext = value;
    }
}
