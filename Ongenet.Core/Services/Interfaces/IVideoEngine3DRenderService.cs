using Ongenet.Engine3D.Abstractions;
using SkiaSharp;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>Offscreen Engine3D frame render for video composition (desktop GPU implementation).</summary>
public interface IVideoEngine3DRenderService
{
    bool IsAvailable { get; }
    SKBitmap? RenderScene(Scene scene, int width, int height);
}
