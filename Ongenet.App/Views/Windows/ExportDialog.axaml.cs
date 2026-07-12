using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.ViewModels;
using Ongenet.Core.Services;

namespace Ongenet.App.Views.Windows;

public partial class ExportDialog : Window
{
    private ExportViewModel? _vm;

    public ExportDialog()
    {
        InitializeComponent();
        Ongenet.App.Accessibility.A11y.Landmark(this,
            Ongenet.App.Localization.Loc.Get("A11y_ExportDialog_Name"),
            Ongenet.App.Localization.Loc.Get("A11y_ExportDialog_Help"));
    }

    public static async Task ShowAsync(Window owner)
    {
        var vm = App.ServiceProvider?.GetRequiredService<ExportViewModel>();
        if (vm is null) return;

        var dialog = new ExportDialog { DataContext = vm, _vm = vm };
        await dialog.ShowDialog(owner);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        string? path;
        if (_vm.Kind == ExportKind.Stems)
        {
            var folder = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose stems output folder",
                AllowMultiple = false
            });
            path = folder.Count > 0 ? folder[0].TryGetLocalPath() : null;
        }
        else
        {
            var ext = _vm.SuggestedFileExtension;
            var types = new List<FilePickerFileType>
            {
                new(_vm.AudioFormat.GetDescription()) { Patterns = new[] { $"*.{ext}" } }
            };
            var suggested = _vm.Kind switch
            {
                ExportKind.Batch => "batch",
                ExportKind.Region => "region",
                _ => "master"
            };
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export audio",
                SuggestedFileName = $"{suggested}.{ext}",
                DefaultExtension = ext,
                FileTypeChoices = types
            });
            path = file?.TryGetLocalPath();
        }

        if (string.IsNullOrEmpty(path)) return;

        await _vm.ExportToPathAsync(path);
        if (!_vm.IsExporting && _vm.Status == "Done.")
            Close();
    }
}
