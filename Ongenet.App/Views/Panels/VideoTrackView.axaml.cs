using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Services;
using Ongenet.App.ViewModels.Panels;

namespace Ongenet.App.Views.Panels;

public partial class VideoTrackView : UserControl
{
    private readonly FrameTicker _ticker;
    private IPlaybackClock? _clock;

    public VideoTrackView()
    {
        InitializeComponent();
        _ticker = new FrameTicker(this, OnTick);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is VideoTrackViewModel vm)
                vm.PickVideoPathAsync = PickVideoPathAsync;
            SyncTickerSpeed();
        };
    }

    private void SyncTickerSpeed()
    {
        var vm = DataContext as VideoTrackViewModel;
        _ticker.SetFast(vm is not null && IsVisible);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            SyncTickerSpeed();
    }

    private void OnTick()
    {
        if (!IsVisible) return;
        (_clock ??= App.ServiceProvider?.GetService<IPlaybackClock>())?.Pump();
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

