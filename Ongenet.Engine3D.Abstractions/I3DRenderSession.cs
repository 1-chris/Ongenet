using System;

namespace Ongenet.Engine3D.Abstractions;

/// <summary>
/// A live rendering session bound to one on-screen control: it owns the GPU resources sized to that
/// control's pixel surface. The control resizes it, submits the latest scene snapshot, and asks it to
/// render a frame. Implementations are provided by the native engine (Ongenet.Engine3D); the control
/// only ever sees this interface.
///
/// <para>Threading: a session is driven from a single renderer thread (the control marshals to it). The
/// returned <see cref="FrameResult"/> is owned by the session and valid until the next call.</para>
/// </summary>
public interface I3DRenderSession : IDisposable
{
    /// <summary>True while the GPU device/resources are healthy; false after an unrecoverable device loss.</summary>
    bool IsValid { get; }

    /// <summary>The presentation path this session produces (CPU readback vs shared-handle interop).</summary>
    FramePresentKind PresentKind { get; }

    /// <summary>Resizes the render target to <paramref name="width"/> x <paramref name="height"/> pixels.</summary>
    void Resize(int width, int height);

    /// <summary>Sets the scene to render on subsequent frames (replaces any previous snapshot).</summary>
    void Submit(SceneSnapshot scene);

    /// <summary>Renders one frame from the last submitted scene and returns how to present it.</summary>
    FrameResult RenderFrame();
}
