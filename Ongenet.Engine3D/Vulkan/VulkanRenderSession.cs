using System;
using Ongenet.Engine3D.Abstractions;
using Ongenet.Engine3D.Rhi;

namespace Ongenet.Engine3D.Vulkan;

/// <summary>
/// Renderer-agnostic <see cref="I3DRenderSession"/> over a Vulkan render target. Holds the latest submitted
/// scene and a reusable BGRA readback buffer; <see cref="RenderFrame"/> draws the scene offscreen and hands
/// the pixels back via the CPU-readback path. An unrecoverable GPU error flips <see cref="IsValid"/> off so
/// the control falls back gracefully rather than throwing into the UI.
/// </summary>
internal sealed class VulkanRenderSession : I3DRenderSession
{
    private readonly IRenderTarget? _target;
    private SceneSnapshot? _scene;
    private byte[] _pixels = Array.Empty<byte>();
    private int _width;
    private int _height;
    private bool _valid;
    private bool _disposed;

    public VulkanRenderSession(VulkanBackend backend, int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _target = backend.CreateRenderTarget(_width, _height);
        _valid = _target is not null;
        EnsurePixelBuffer();
    }

    public bool IsValid => _valid && !_disposed && _target is not null;

    public FramePresentKind PresentKind => FramePresentKind.Cpu;

    public void Resize(int width, int height)
    {
        if (!IsValid) return;
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height) return;
        _width = width;
        _height = height;
        try
        {
            _target!.Resize(_width, _height);
            EnsurePixelBuffer();
        }
        catch
        {
            _valid = false;
        }
    }

    public void Submit(SceneSnapshot scene) => _scene = scene;

    public FrameResult RenderFrame()
    {
        if (!IsValid || _scene is null) return FrameResult.Nothing;
        try
        {
            _target!.Render(_scene);
            var stride = _target.Readback(_pixels);
            return FrameResult.FromPixels(_pixels, _width, _height, stride);
        }
        catch
        {
            _valid = false;
            return FrameResult.Nothing;
        }
    }

    private void EnsurePixelBuffer()
    {
        var needed = _width * _height * 4;
        if (_pixels.Length < needed) _pixels = new byte[needed];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _target?.Dispose();
    }
}
