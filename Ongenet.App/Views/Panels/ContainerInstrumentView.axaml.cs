using System;
using Avalonia.Controls;
using Ongenet.App.ViewModels.Instruments;

namespace Ongenet.App.Views.Panels;

public partial class ContainerInstrumentView : UserControl
{
    public ContainerInstrumentView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
            IsVisible = DataContext is ContainerInstrumentViewModel;
    }

    private void OnXyDragStarted(object? sender, EventArgs e)
    {
        if (DataContext is ContainerInstrumentViewModel vm)
            vm.BeginXyAdjust();
    }
}
