using Avalonia.Controls;
using Ongenet.VideoComposition.Editor.Controls;
using Ongenet.VideoComposition.Editor.Preview;

namespace Ongenet.App.Platform;

public interface IVideoCompositionEditorFactory
{
    bool IsAvailable { get; }
    Control CreatePreviewCanvas();
}

public sealed class NullVideoCompositionEditorFactory : IVideoCompositionEditorFactory
{
    public bool IsAvailable => false;

    public Control CreatePreviewCanvas() => new Border
    {
        Child = new TextBlock { Text = "Video preview is not available on this platform." }
    };
}
