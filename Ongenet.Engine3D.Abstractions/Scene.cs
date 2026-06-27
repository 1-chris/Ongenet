using System.Collections.Generic;
using System.Numerics;

namespace Ongenet.Engine3D.Abstractions;

/// <summary>
/// The editable scene description: a transform tree (<see cref="Root"/>), a <see cref="Camera"/>, lights
/// and a clear colour. The UI mutates this; calling <see cref="SceneSnapshot.Capture"/> produces the
/// immutable per-frame snapshot the renderer consumes, so the two threads never share mutable state.
/// </summary>
public sealed class Scene
{
    /// <summary>The transform-tree root (its own transform applies to everything).</summary>
    public SceneNode Root { get; } = new() { Name = "root" };

    public Camera Camera { get; } = new();

    public List<Light> Lights { get; } = new()
    {
        Light.Ambient(0.25f),
        Light.Directional(Vector3.Normalize(new Vector3(-0.4f, -1f, -0.35f)))
    };

    /// <summary>Background clear colour (linear RGBA, premultiplied semantics handled by the backend).</summary>
    public Vector4 ClearColor { get; set; } = new(0.07f, 0.07f, 0.09f, 1f);
}
