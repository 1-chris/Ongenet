using System.Collections.Generic;
using System.Numerics;

namespace Ongenet.Engine3D.Abstractions;

/// <summary>
/// A node in the scene's transform tree. It carries a local TRS transform and, optionally, a mesh +
/// material to draw at that transform. Children inherit the parent's world transform. This is a mutable,
/// UI-thread object; it is flattened into an immutable <see cref="SceneSnapshot"/> for the render thread.
/// </summary>
public sealed class SceneNode
{
    public string Name { get; set; } = "node";

    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>Geometry drawn at this node (null = a pure transform/group node).</summary>
    public MeshData? Mesh { get; set; }

    /// <summary>Surface for <see cref="Mesh"/> (a default material is used when null).</summary>
    public Material? Material { get; set; }

    /// <summary>Whether this node (and its mesh) is drawn. Children are still traversed.</summary>
    public bool Visible { get; set; } = true;

    public List<SceneNode> Children { get; } = new();

    /// <summary>Local TRS matrix (scale, then rotate, then translate).</summary>
    public Matrix4x4 LocalMatrix()
        => Matrix4x4.CreateScale(Scale)
           * Matrix4x4.CreateFromQuaternion(Rotation)
           * Matrix4x4.CreateTranslation(Position);

    public SceneNode AddChild(SceneNode child)
    {
        Children.Add(child);
        return child;
    }
}
