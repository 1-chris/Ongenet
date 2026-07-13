using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.App.Localization;
using Ongenet.App.ViewModels.Panels;

namespace Ongenet.App.Views.Panels;

public partial class VideoResourcesView : UserControl
{
    public VideoResourcesView()
    {
        InitializeComponent();
        AddHandler(InputElement.DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
        AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right) return;
        if (DataContext is not VideoResourcesViewModel vm) return;
        if (e.Source is not Control { DataContext: VideoResourceNode node }) return;
        if (node.Kind != VideoResourceKind.Layer || node.Layer is null) return;
        vm.SelectedNode = node;
        ShowLayerContextMenu(vm);
        e.Handled = true;
    }

    private void ShowLayerContextMenu(VideoResourcesViewModel vm)
    {
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem
                {
                    Header = Loc.Get("VideoTimeline_Move_layer_up", "Move layer up"),
                    Command = vm.MoveLayerUpCommand
                },
                new MenuItem
                {
                    Header = Loc.Get("VideoTimeline_Move_layer_down", "Move layer down"),
                    Command = vm.MoveLayerDownCommand
                },
                new MenuItem
                {
                    Header = Loc.Get("VideoTimeline_Delete_layer", "Delete layer"),
                    Command = vm.DeleteLayerCommand
                }
            }
        };
        menu.Open(this);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not VideoResourcesViewModel vm) return;
        if (e.Source is not Control { DataContext: VideoResourceNode node }) return;
        if (node.Kind != VideoResourceKind.LinkedClip || node.LinkedClip is null) return;
        vm.SeekLinkedClip(node.LinkedClip);
        e.Handled = true;
    }
}
