using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Ongenet.App.ViewModels.Panels;

namespace Ongenet.App.Views.Panels;

public partial class NotationView : UserControl
{
    public NotationView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is NotationViewModel vm)
                Staff.ScoreEdited += vm.OnScoreEdited;
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is NotationViewModel vm)
            {
                vm.PickSavePathAsync = PickSavePathAsync;
                vm.PickOpenPathAsync = PickOpenPathAsync;
                vm.PickSavePdfPathAsync = PickSavePdfPathAsync;
            }
        };
    }

    private async Task<string?> PickOpenPathAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import MusicXML",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("MusicXML") { Patterns = ["*.musicxml", "*.xml", "*.mxl"] }
            ]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickSavePathAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export MusicXML",
            SuggestedFileName = "score.musicxml",
            FileTypeChoices =
            [
                new FilePickerFileType("MusicXML") { Patterns = ["*.musicxml", "*.xml"] }
            ]
        });
        return file?.TryGetLocalPath();
    }

    private async Task<string?> PickSavePdfPathAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export score PDF",
            SuggestedFileName = "score.pdf",
            FileTypeChoices =
            [
                new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }
            ]
        });
        return file?.TryGetLocalPath();
    }
}
