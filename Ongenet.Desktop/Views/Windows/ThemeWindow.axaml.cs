using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ongenet.Desktop.ViewModels;

namespace Ongenet.Desktop.Views.Windows;

/// <summary>
/// Live theme editor: pick a Catppuccin flavour, flip light/dark, edit any palette token by hex, and
/// import/export themes as JSON. Mirrors the custom chrome used by the log window.
/// </summary>
public partial class ThemeWindow : Window
{
    private static readonly FilePickerFileType JsonFileType =
        new("Theme JSON") { Patterns = new[] { "*.json" } };

    public ThemeWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(ThemeEditorViewModel viewModel) => DataContext = viewModel;

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

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeEditorViewModel vm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export theme",
            SuggestedFileName = $"{vm.CurrentName}.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { JsonFileType }
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(vm.ExportCurrentJson());
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeEditorViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import theme",
            AllowMultiple = false,
            FileTypeFilter = new[] { JsonFileType }
        });
        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        vm.ApplyJson(json);
    }
}
