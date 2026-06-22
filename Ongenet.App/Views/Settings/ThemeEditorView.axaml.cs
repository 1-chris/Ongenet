using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Views.Settings;

/// <summary>
/// Live theme editor (pick a Catppuccin flavour, flip light/dark, edit palette tokens, import/export
/// JSON) hosted in the Settings window's Theme tab. Extracted from the former standalone theme window.
/// </summary>
public partial class ThemeEditorView : UserControl
{
    private static readonly FilePickerFileType JsonFileType =
        new("Theme JSON") { Patterns = new[] { "*.json" } };

    public ThemeEditorView()
    {
        InitializeComponent();
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeEditorViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
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
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
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
