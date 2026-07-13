using Avalonia.Controls;

namespace Ongenet.App.Views.Windows;

public partial class VideoPreviewWindow : Window
{
    public VideoPreviewWindow() => InitializeComponent();

    public void InvalidatePreview() => PreviewImage.InvalidateVisual();
}
