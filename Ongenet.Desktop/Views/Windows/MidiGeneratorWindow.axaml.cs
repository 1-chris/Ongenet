using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.Desktop.ViewModels;

namespace Ongenet.Desktop.Views.Windows;

/// <summary>
/// Window for generating randomized chord progressions into the piano-roll clip. Uses the same
/// custom chrome as the history/theme windows; button clicks delegate to <see cref="MidiGeneratorViewModel"/>.
/// </summary>
public partial class MidiGeneratorWindow : ChromedWindow
{
    public MidiGeneratorWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(MidiGeneratorViewModel viewModel) => DataContext = viewModel;

    private void Generate_Click(object? sender, RoutedEventArgs e)
        => (DataContext as MidiGeneratorViewModel)?.Generate();

    private void Insert_Click(object? sender, RoutedEventArgs e)
        => (DataContext as MidiGeneratorViewModel)?.Insert();

    private void Replace_Click(object? sender, RoutedEventArgs e)
        => (DataContext as MidiGeneratorViewModel)?.Replace();

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
