using Avalonia.Controls;
using Ongenet.App.Platform;
using Ongenet.VideoComposition.Editor.Controls;

namespace Ongenet.Desktop;

public sealed class VideoCompositionEditorFactory : IVideoCompositionEditorFactory
{
    public bool IsAvailable => true;

    public Control CreatePreviewCanvas() => new VideoCompositionCanvas();
}
