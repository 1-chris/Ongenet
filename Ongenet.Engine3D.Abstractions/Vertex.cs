using System.Numerics;
using System.Runtime.InteropServices;

namespace Ongenet.Engine3D.Abstractions;

/// <summary>
/// A single mesh vertex: position, normal (for lighting) and a per-vertex colour. The layout is
/// explicit (sequential, tightly packed) so a backend can upload the array straight into a GPU
/// vertex buffer and describe it with a matching vertex-input layout without any repacking.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct Vertex
{
    public readonly Vector3 Position;
    public readonly Vector3 Normal;
    public readonly Vector4 Color;

    public Vertex(Vector3 position, Vector3 normal, Vector4 color)
    {
        Position = position;
        Normal = normal;
        Color = color;
    }

    /// <summary>Size of one vertex in bytes (the vertex-buffer stride): 3 + 3 + 4 floats.</summary>
    public const int SizeInBytes = (3 + 3 + 4) * sizeof(float);

    /// <summary>Byte offset of <see cref="Position"/> within the vertex.</summary>
    public const int PositionOffset = 0;

    /// <summary>Byte offset of <see cref="Normal"/> within the vertex.</summary>
    public const int NormalOffset = 3 * sizeof(float);

    /// <summary>Byte offset of <see cref="Color"/> within the vertex.</summary>
    public const int ColorOffset = 6 * sizeof(float);
}
