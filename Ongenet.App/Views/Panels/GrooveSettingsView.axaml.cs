using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Ongenet.App.ViewModels.Panels;

namespace Ongenet.App.Views.Panels;

public partial class GrooveSettingsView : UserControl
{
    public GrooveSettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GrooveSettingsViewModel vm)
                vm.PickGroovePathAsync = PickGroovePathAsync;
        };
    }

    private async System.Threading.Tasks.Task<string?> PickGroovePathAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import groove",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Ongenet groove") { Patterns = ["*.ongenet-groove", "*.json"] },
                FilePickerFileTypes.All
            ]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
