using System;

namespace Ongenet.Engine3D.Abstractions;

/// <summary>How a rendered frame is handed back to the Avalonia presenter.</summary>
public enum FramePresentKind
{
    /// <summary>Nothing new this frame (e.g. render skipped); the presenter keeps the previous image.</summary>
    None,

    /// <summary>CPU pixels in <see cref="FrameResult.Pixels"/> (BGRA8888) - the universal readback path.</summary>
    Cpu,

    /// <summary>A GPU texture shared via a platform handle - the zero-copy compositor-interop path.</summary>
    SharedHandle
}

/// <summary>
/// The product of one <see cref="I3DRenderSession.RenderFrame"/>. For the readback path it points at the
/// session's reusable BGRA pixel buffer (do not retain it past the next render); for the interop path it
/// carries the shared image handle + synchronisation indices the compositor needs.
/// </summary>
public readonly struct FrameResult
{
    public FramePresentKind Kind { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    // --- CPU readback path ---
    /// <summary>BGRA8888 (premultiplied) pixels, row-major, length = <see cref="Height"/> * <see cref="Stride"/>.</summary>
    public byte[]? Pixels { get; init; }

    /// <summary>Row stride in bytes for <see cref="Pixels"/>.</summary>
    public int Stride { get; init; }

    // --- Shared-handle (interop) path ---
    /// <summary>Native shared image handle (e.g. an opaque-fd / Win32 NT handle), backend specific.</summary>
    public nint ImageHandle { get; init; }

    /// <summary>The handle type, matching Avalonia's KnownPlatformGraphicsExternalImageHandleTypes.</summary>
    public string? HandleType { get; init; }

    /// <summary>Keyed-mutex acquire/release keys (when the backend synchronises via a keyed mutex).</summary>
    public uint MutexAcquireKey { get; init; }
    public uint MutexReleaseKey { get; init; }

    public static FrameResult Nothing => new() { Kind = FramePresentKind.None };

    public static FrameResult FromPixels(byte[] pixels, int width, int height, int stride) => new()
    {
        Kind = FramePresentKind.Cpu,
        Pixels = pixels,
        Width = width,
        Height = height,
        Stride = stride
    };
}
