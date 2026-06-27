using System.Numerics;

namespace Ongenet.Engine3D.Abstractions;

/// <summary>
/// A simple physically-inspired surface description. The reference renderer uses
/// <see cref="BaseColor"/> with <see cref="Metallic"/>/<see cref="Roughness"/> for a cheap lit look and
/// adds <see cref="Emissive"/> unconditionally (useful for glowing indicators on a control). Per-vertex
/// colours in the mesh are multiplied with <see cref="BaseColor"/>.
/// </summary>
public sealed class Material
{
    /// <summary>Linear RGBA base/albedo colour (alpha enables blending where the renderer supports it).</summary>
    public Vector4 BaseColor { get; set; } = new(0.8f, 0.8f, 0.8f, 1f);

    /// <summary>0 = dielectric, 1 = metal.</summary>
    public float Metallic { get; set; } = 0.1f;

    /// <summary>0 = mirror-smooth, 1 = fully rough.</summary>
    public float Roughness { get; set; } = 0.5f;

    /// <summary>Self-illumination added after lighting (linear RGB).</summary>
    public Vector3 Emissive { get; set; } = Vector3.Zero;

    public Material Clone() => new()
    {
        BaseColor = BaseColor,
        Metallic = Metallic,
        Roughness = Roughness,
        Emissive = Emissive
    };
}
