using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.Desktop.ViewModels;

namespace Ongenet.Desktop.Views.Windows;

/// <summary>
/// Lists the undo/redo timeline; selecting an entry jumps the project to that point (bulk undo/redo).
/// Uses the same custom chrome as the log and theme windows.
/// </summary>
public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(HistoryViewModel viewModel) => DataContext = viewModel;

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
