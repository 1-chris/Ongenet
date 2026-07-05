using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Ongenet.App.ViewModels.Field;

namespace Ongenet.App.Views.Windows;

/// <summary>
/// A freely-resizable standalone window hosting the full Field node-graph editor. It shares the same
/// <see cref="FieldEditorViewModel"/> (and therefore the same live graph) as the embedded editor, so edits
/// in either place drive the same audio. Mirrors <see cref="Engine3DVisualWindow"/>'s chrome.
/// </summary>
public partial class FieldWindow : ChromedWindow
{
    public FieldWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Sets the window title and the editor view-model to host.</summary>
    public void Configure(string title, FieldEditorViewModel editor)
    {
        Title = title;
        if (this.FindControl<TextBlock>("TitleText") is { } t) t.Text = title;
        if (this.FindControl<Ongenet.App.Views.Field.FieldEditorView>("Editor") is { } view)
        {
            view.ShowPopOut = false;
            view.DataContext = editor;
        }
    }

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
