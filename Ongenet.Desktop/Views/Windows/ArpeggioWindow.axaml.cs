using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.Desktop.ViewModels;

namespace Ongenet.Desktop.Views.Windows;

/// <summary>
/// Window for "Convert to arpeggio": arpeggiates the piano-roll's selected notes. Uses the same
/// custom chrome as the other tool windows; Apply delegates to <see cref="ArpeggiatorViewModel"/>.
/// </summary>
public partial class ArpeggioWindow : ChromedWindow
{
    public ArpeggioWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(ArpeggiatorViewModel viewModel) => DataContext = viewModel;

    private void Apply_Click(object? sender, RoutedEventArgs e)
        => (DataContext as ArpeggiatorViewModel)?.Apply();

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control c || c is Button || c.Parent is Button) return;
        BeginMoveDrag(e);
    }

    private void OnResizeHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string tag) return;
        var edge = tag switch
        {
            "Left" => WindowEdge.West,
            "Right" => WindowEdge.East,
            "Top" => WindowEdge.North,
            "Bottom" => WindowEdge.South,
            "TopLeft" => WindowEdge.NorthWest,
            "TopRight" => WindowEdge.NorthEast,
            "BottomLeft" => WindowEdge.SouthWest,
            "BottomRight" => WindowEdge.SouthEast,
            _ => WindowEdge.North
        };
        BeginResizeDrag(edge, e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
