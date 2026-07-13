using System;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;
using SkiaSharp;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>Renders live Engine3D video layers (scope, cubes, particles) to premultiplied BGRA bitmaps.</summary>
public interface IVideoEngine3DLayerRenderer
{
    bool IsAvailable { get; }
    SKBitmap? RenderWaveformLayer(VideoLayer layer, IVideoAudioScopeService scope, int width, int height, double dt);
    SKBitmap? RenderEngine3DLayer(VideoLayer layer, IVideoAudioScopeService scope, int width, int height, double dt);
    void ClearLayerState(Guid layerId);
    void ClearAll();
}
