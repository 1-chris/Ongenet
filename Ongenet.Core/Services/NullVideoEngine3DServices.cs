using System;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.Core.Services;

public sealed class NullVideoEngine3DRenderService : IVideoEngine3DRenderService
{
    public bool IsAvailable => false;
    public SkiaSharp.SKBitmap? RenderScene(Scene scene, int width, int height) => null;
}

public sealed class NullVideoEngine3DLayerRenderer : IVideoEngine3DLayerRenderer
{
    public bool IsAvailable => false;
    public SkiaSharp.SKBitmap? RenderWaveformLayer(Models.Media.VideoLayer layer, IVideoAudioScopeService scope,
        int width, int height, double dt) => null;
    public SkiaSharp.SKBitmap? RenderEngine3DLayer(Models.Media.VideoLayer layer, IVideoAudioScopeService scope,
        int width, int height, double dt) => null;
    public void ClearLayerState(Guid layerId) { }
    public void ClearAll() { }
}
