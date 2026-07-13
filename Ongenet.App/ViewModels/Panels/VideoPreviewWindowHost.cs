using Avalonia;
using Avalonia.Controls;
using Ongenet.App.Views.Windows;

namespace Ongenet.App.ViewModels.Panels;

/// <summary>Manages the detachable video preview window.</summary>
public sealed class VideoPreviewWindowHost
{
    private readonly VideoTrackViewModel _vm;
    private VideoPreviewWindow? _window;

    public VideoPreviewWindowHost(VideoTrackViewModel vm) => _vm = vm;

    public void ShowOrActivate()
    {
        if (_window is null)
        {
            _window = new VideoPreviewWindow { DataContext = _vm };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
        {
            _window.Activate();
        }
    }

    public void UpdateFrame() => _window?.InvalidatePreview();
}
