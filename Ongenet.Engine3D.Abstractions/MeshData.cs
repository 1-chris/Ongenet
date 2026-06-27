using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Ongenet.Engine3D.Abstractions;

/// <summary>
/// CPU-side geometry: an indexed triangle list (<see cref="Vertices"/> + <see cref="Indices"/>). It is
/// immutable and carries a stable <see cref="Id"/> so a renderer can cache the uploaded GPU buffers and
/// reuse them across frames (the snapshot only references the mesh, it never re-uploads unchanged data).
/// Use the <c>Box</c>/<c>Sphere</c>/<c>Plane</c> factories for primitives, or build one from raw arrays.
/// </summary>
public sealed class MeshData
{
    private static int _nextId;

    private Vertex[] _vertices;
    private int _revision;

    public MeshData(Vertex[] vertices, uint[] indices)
    {
        _vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        Indices = indices ?? throw new ArgumentNullException(nameof(indices));
        Id = Interlocked.Increment(ref _nextId);
    }

    /// <summary>Process-unique identity used by renderers to key cached GPU buffers.</summary>
    public int Id { get; }

    /// <summary>The current vertex array. For a dynamic mesh this reference is swapped by <see cref="UpdateVertices"/>.</summary>
    public Vertex[] Vertices => _vertices;

    public uint[] Indices { get; }

    public int VertexCount => _vertices.Length;
    public int IndexCount => Indices.Length;

    /// <summary>
    /// Bumped each time the geometry changes. A renderer caches GPU buffers by <see cref="Id"/> and
    /// re-uploads when the revision it last saw differs, so a dynamic mesh reuses one GPU buffer.
    /// </summary>
    public int Revision => Volatile.Read(ref _revision);

    /// <summary>
    /// Replaces the vertices of a dynamic mesh (must keep the same vertex count, so the GPU buffer size is
    /// stable). Publishes a brand-new array by reference so a renderer thread reading concurrently always
    /// sees a complete, consistent array (no torn reads) - never mutate a previously-published array in place.
    /// </summary>
    public void UpdateVertices(Vertex[] vertices)
    {
        if (vertices is null) throw new ArgumentNullException(nameof(vertices));
        if (vertices.Length != _vertices.Length)
            throw new ArgumentException("Dynamic mesh vertex count must stay constant.", nameof(vertices));
        _vertices = vertices;
        Interlocked.Increment(ref _revision);
    }

    /// <summary>Creates a dynamic mesh with a fixed vertex capacity (zeroed) and the given index topology.</summary>
    public static MeshData CreateDynamic(int vertexCount, uint[] indices)
        => new(new Vertex[vertexCount], indices);

    /// <summary>A unit cube centred on the origin (side length 1), with per-face normals.</summary>
    public static MeshData Box(float size = 1f, Vector4? color = null)
    {
        var c = color ?? new Vector4(1f, 1f, 1f, 1f);
        var h = size * 0.5f;
        var verts = new List<Vertex>(24);
        var idx = new List<uint>(36);

        void Face(Vector3 normal, Vector3 a, Vector3 b, Vector3 d, Vector3 e)
        {
            var start = (uint)verts.Count;
            verts.Add(new Vertex(a, normal, c));
            verts.Add(new Vertex(b, normal, c));
            verts.Add(new Vertex(d, normal, c));
            verts.Add(new Vertex(e, normal, c));
            idx.Add(start); idx.Add(start + 1); idx.Add(start + 2);
            idx.Add(start); idx.Add(start + 2); idx.Add(start + 3);
        }

        Face(new Vector3(0, 0, 1), new Vector3(-h, -h, h), new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h));
        Face(new Vector3(0, 0, -1), new Vector3(h, -h, -h), new Vector3(-h, -h, -h), new Vector3(-h, h, -h), new Vector3(h, h, -h));
        Face(new Vector3(1, 0, 0), new Vector3(h, -h, h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(h, h, h));
        Face(new Vector3(-1, 0, 0), new Vector3(-h, -h, -h), new Vector3(-h, -h, h), new Vector3(-h, h, h), new Vector3(-h, h, -h));
        Face(new Vector3(0, 1, 0), new Vector3(-h, h, h), new Vector3(h, h, h), new Vector3(h, h, -h), new Vector3(-h, h, -h));
        Face(new Vector3(0, -1, 0), new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h), new Vector3(-h, -h, h));

        return new MeshData(verts.ToArray(), idx.ToArray());
    }

    /// <summary>A flat quad on the XZ plane, centred on the origin, normal pointing +Y.</summary>
    public static MeshData Plane(float size = 1f, Vector4? color = null)
    {
        var c = color ?? new Vector4(1f, 1f, 1f, 1f);
        var h = size * 0.5f;
        var n = new Vector3(0, 1, 0);
        var verts = new[]
        {
            new Vertex(new Vector3(-h, 0, -h), n, c),
            new Vertex(new Vector3(h, 0, -h), n, c),
            new Vertex(new Vector3(h, 0, h), n, c),
            new Vertex(new Vector3(-h, 0, h), n, c)
        };
        var idx = new uint[] { 0, 2, 1, 0, 3, 2 };
        return new MeshData(verts, idx);
    }

    /// <summary>A UV sphere of the given radius, tessellated into <paramref name="segments"/> bands.</summary>
    public static MeshData Sphere(float radius = 0.5f, int segments = 24, Vector4? color = null)
    {
        var c = color ?? new Vector4(1f, 1f, 1f, 1f);
        segments = Math.Max(3, segments);
        var rings = segments;
        var sectors = segments * 2;
        var verts = new List<Vertex>((rings + 1) * (sectors + 1));
        var idx = new List<uint>(rings * sectors * 6);

        for (var r = 0; r <= rings; r++)
        {
            var phi = MathF.PI * r / rings;            // 0..pi (top to bottom)
            var y = MathF.Cos(phi);
            var ringRadius = MathF.Sin(phi);
            for (var s = 0; s <= sectors; s++)
            {
                var theta = 2f * MathF.PI * s / sectors;
                var x = ringRadius * MathF.Cos(theta);
                var z = ringRadius * MathF.Sin(theta);
                var normal = new Vector3(x, y, z);
                verts.Add(new Vertex(normal * radius, normal, c));
            }
        }

        var stride = sectors + 1;
        for (var r = 0; r < rings; r++)
        {
            for (var s = 0; s < sectors; s++)
            {
                var a = (uint)(r * stride + s);
                var b = (uint)((r + 1) * stride + s);
                idx.Add(a); idx.Add(b); idx.Add(a + 1);
                idx.Add(a + 1); idx.Add(b); idx.Add(b + 1);
            }
        }

        return new MeshData(verts.ToArray(), idx.ToArray());
    }
}
