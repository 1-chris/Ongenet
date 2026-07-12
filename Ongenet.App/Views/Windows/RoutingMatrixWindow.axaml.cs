using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.ViewModels.Panels;

namespace Ongenet.App.Views.Windows;

public partial class RoutingMatrixWindow : Window
{
    public RoutingMatrixWindow()
    {
        InitializeComponent();
    }

    public static void ShowMatrix(Window? owner)
    {
        var vm = App.ServiceProvider?.GetRequiredService<RoutingMatrixViewModel>();
        if (vm is null) return;

        var window = new RoutingMatrixWindow { DataContext = vm };
        if (owner is not null) window.Show(owner);
        else window.Show();
    }
}
