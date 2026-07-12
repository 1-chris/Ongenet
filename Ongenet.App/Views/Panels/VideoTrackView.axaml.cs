using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Ongenet.App.ViewModels.Panels;

namespace Ongenet.App.Views.Panels;

public partial class VideoTrackView : UserControl
{
    public VideoTrackView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is VideoTrackViewModel vm)
                vm.PickVideoPathAsync = PickVideoPathAsync;
        };
    }

    private async Task<string?> PickVideoPathAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open video",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Video") { Patterns = ["*.mp4", "*.mov", "*.mkv", "*.avi", "*.webm"] },
                FilePickerFileTypes.All
            ]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
