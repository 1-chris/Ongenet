using System;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.Engine3D.Rhi;

/// <summary>
/// The Render Hardware Interface seam: a GPU backend that can create offscreen render targets and draw
/// scenes into them. One implementation exists today (<c>Vulkan.VulkanBackend</c>, native on Windows/Linux
/// and macOS-via-MoltenVK); a Direct3D 12 or native Metal backend could be added behind this same seam
/// without touching the renderer-agnostic engine session, the Avalonia control, or the scene model.
/// </summary>
public interface IRenderBackend : IDisposable
{
    /// <summary>Short human-readable name for diagnostics (e.g. "Vulkan (MoltenVK)").</summary>
    string Name { get; }

    /// <summary>True once the device initialised successfully and targets can be created.</summary>
    bool IsInitialized { get; }

    /// <summary>Creates an offscreen render target of the given pixel size, or null on failure.</summary>
    IRenderTarget? CreateRenderTarget(int width, int height);
}

/// <summary>
/// An offscreen color+depth render target. The owner renders a scene snapshot into it and then reads the
/// color buffer back to system memory (BGRA8888) for the CPU-readback presenter. (A future zero-copy path
/// will expose the underlying GPU image handle instead.)
/// </summary>
public interface IRenderTarget : IDisposable
{
    int Width { get; }
    int Height { get; }

    /// <summary>Re-allocates the GPU images to a new pixel size (no-op if unchanged).</summary>
    void Resize(int width, int height);

    /// <summary>Renders <paramref name="scene"/> into the target's color buffer.</summary>
    void Render(SceneSnapshot scene);

    /// <summary>
    /// Copies the last-rendered color buffer (BGRA8888, premultiplied) into <paramref name="destination"/>,
    /// which must be at least <see cref="Height"/> * stride bytes. Returns the row stride in bytes.
    /// </summary>
    int Readback(byte[] destination);
}
