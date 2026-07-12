using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Localization;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Views.Windows;

/// <summary>Lists user scripts and runs them against the live project via Roslyn.</summary>
public partial class ScriptsWindow : ChromedWindow
{
    public ScriptsWindow()
    {
        InitializeComponent();
        DataContext = App.ServiceProvider?.GetRequiredService<ScriptsViewModel>()
            ?? throw new InvalidOperationException("ScriptsViewModel is not registered.");
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

    private async void LoadScript_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScriptsViewModel vm) return;
        var path = await PickScriptFileAsync();
        if (path is not null)
            vm.LoadScript(path);
    }

    private async Task<string?> PickScriptFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("Scripts_Load_dialog_title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("Scripts_CSharp_files"))
                {
                    Patterns = ["*.cs"]
                }
            ]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
