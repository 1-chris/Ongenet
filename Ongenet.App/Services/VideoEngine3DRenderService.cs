using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Controls.Engine3D;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Engine3D.Abstractions;
using SkiaSharp;

namespace Ongenet.App.Services;

/// <summary>Desktop GPU offscreen render of a <see cref="Scene"/> to premultiplied BGRA.</summary>
public sealed class VideoEngine3DRenderService : IVideoEngine3DRenderService, IDisposable
{
    private readonly I3DEngineFactory? _factory;
    private Engine3DRenderLoop? _loop;
    private I3DRenderSession? _session;
    private int _sessionW;
    private int _sessionH;

    public VideoEngine3DRenderService()
    {
        _factory = App.ServiceProvider?.GetService<I3DEngineFactory>();
    }

    public bool IsAvailable => _factory?.IsAvailable == true;

    public SKBitmap? RenderScene(Scene scene, int width, int height)
    {
        if (!IsAvailable || _factory is null) return null;

        width = Math.Clamp(width, 16, 4096);
        height = Math.Clamp(height, 16, 4096);
        EnsureSession(width, height);
        if (_loop is null) return null;

        _loop.Submit(SceneSnapshot.Capture(scene), width, height);

        FrameBuffer? frame = null;
        for (var i = 0; i < 80; i++)
        {
            frame = _loop.AcquireFrame();
            if (frame is not null) break;
            Thread.Sleep(25);
        }

        if (frame is null) return null;
        try
        {
            return FrameToBitmap(frame);
        }
        finally
        {
            _loop.ReleaseFrame();
        }
    }

    private void EnsureSession(int width, int height)
    {
        if (_loop is not null && _sessionW == width && _sessionH == height) return;

        _loop?.Dispose();
        _loop = null;
        _session?.Dispose();
        _session = null;

        _session = _factory!.CreateSession(width, height);
        if (_session is null) return;

        _sessionW = width;
        _sessionH = height;
        _loop = new Engine3DRenderLoop(_session);
    }

    internal static SKBitmap? FrameToBitmap(FrameBuffer frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0) return null;
        var bitmap = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var rowBytes = frame.Width * 4;
        var dstPtr = bitmap.GetPixels();
        for (var y = 0; y < frame.Height; y++)
        {
            var srcOffset = y * frame.Stride;
            if (srcOffset + rowBytes > frame.Pixels.Length) break;
            System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, srcOffset, dstPtr + y * bitmap.RowBytes, rowBytes);
        }

        return bitmap;
    }

    public void Dispose()
    {
        _loop?.Dispose();
        _loop = null;
        _session?.Dispose();
        _session = null;
    }
}
