using System.Collections.Generic;
using System.Numerics;

namespace Ongenet.Engine3D.Abstractions;

/// <summary>
/// An immutable, flattened copy of a <see cref="Scene"/> for one frame. Transforms are pre-multiplied to
/// world space and material/light/camera values are captured by value, so the render thread can consume it
/// without touching the mutable scene the UI thread is editing. This mirrors the lock-free
/// "commit a snapshot for the audio thread" pattern used elsewhere in the codebase (e.g. Track.CommitAutoLanes).
/// </summary>
public sealed class SceneSnapshot
{
    public SceneSnapshot(Vector4 clearColor, CameraSnapshot camera,
        IReadOnlyList<DrawItem> items, Vector3 ambient, Vector3 lightDirection, Vector3 lightColor)
    {
        ClearColor = clearColor;
        Camera = camera;
        Items = items;
        Ambient = ambient;
        LightDirection = lightDirection;
        LightColor = lightColor;
    }

    public Vector4 ClearColor { get; }
    public CameraSnapshot Camera { get; }
    public IReadOnlyList<DrawItem> Items { get; }

    /// <summary>Aggregated ambient term (sum of ambient lights, colour*intensity).</summary>
    public Vector3 Ambient { get; }

    /// <summary>The (single) directional light's travel direction, normalised.</summary>
    public Vector3 LightDirection { get; }

    /// <summary>The directional light's colour*intensity.</summary>
    public Vector3 LightColor { get; }

    /// <summary>Walks the scene tree, flattening it into world-space draw items + captured camera/lights.</summary>
    public static SceneSnapshot Capture(Scene scene)
    {
        var items = new List<DrawItem>();
        Flatten(scene.Root, Matrix4x4.Identity, items);

        var ambient = Vector3.Zero;
        var lightDir = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.35f));
        var lightColor = Vector3.Zero;
        foreach (var light in scene.Lights)
        {
            if (light.Kind == LightKind.Ambient)
            {
                ambient += light.Color * light.Intensity;
            }
            else
            {
                var d = light.Direction.LengthSquared() > 1e-6f ? Vector3.Normalize(light.Direction) : lightDir;
                lightDir = d;
                lightColor += light.Color * light.Intensity;
            }
        }

        var cam = scene.Camera;
        var camSnap = new CameraSnapshot(cam.ViewMatrix(), cam.Position, cam.Projection,
            cam.FieldOfView, cam.OrthographicHalfHeight, cam.NearPlane, cam.FarPlane);

        return new SceneSnapshot(scene.ClearColor, camSnap, items, ambient, lightDir, lightColor);
    }

    private static void Flatten(SceneNode node, Matrix4x4 parentWorld, List<DrawItem> items)
    {
        var world = node.LocalMatrix() * parentWorld;
        if (node.Visible && node.Mesh is { } mesh)
        {
            var m = node.Material ?? DefaultMaterial;
            items.Add(new DrawItem(world, mesh, m.BaseColor, m.Metallic, m.Roughness, m.Emissive));
        }

        foreach (var child in node.Children)
            Flatten(child, world, items);
    }

    private static readonly Material DefaultMaterial = new();
}

/// <summary>Captured camera state; projection is computed against the actual target aspect at render time.</summary>
public readonly struct CameraSnapshot
{
    public CameraSnapshot(Matrix4x4 view, Vector3 position, CameraProjection projection,
        float fieldOfView, float orthographicHalfHeight, float nearPlane, float farPlane)
    {
        View = view;
        Position = position;
        Projection = projection;
        FieldOfView = fieldOfView;
        OrthographicHalfHeight = orthographicHalfHeight;
        NearPlane = nearPlane;
        FarPlane = farPlane;
    }

    public Matrix4x4 View { get; }
    public Vector3 Position { get; }
    public CameraProjection Projection { get; }
    public float FieldOfView { get; }
    public float OrthographicHalfHeight { get; }
    public float NearPlane { get; }
    public float FarPlane { get; }

    public Matrix4x4 ProjectionMatrix(float aspect)
    {
        if (aspect <= 0) aspect = 1f;
        if (Projection == CameraProjection.Orthographic)
        {
            var halfH = OrthographicHalfHeight < 0.01f ? 0.01f : OrthographicHalfHeight;
            return Matrix4x4.CreateOrthographic(halfH * 2f * aspect, halfH * 2f, NearPlane, FarPlane);
        }

        return Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, NearPlane, FarPlane);
    }
}

/// <summary>One world-space mesh draw with its captured material values.</summary>
public readonly struct DrawItem
{
    public DrawItem(Matrix4x4 world, MeshData mesh, Vector4 baseColor, float metallic, float roughness, Vector3 emissive)
    {
        World = world;
        Mesh = mesh;
        BaseColor = baseColor;
        Metallic = metallic;
        Roughness = roughness;
        Emissive = emissive;
    }

    public Matrix4x4 World { get; }
    public MeshData Mesh { get; }
    public Vector4 BaseColor { get; }
    public float Metallic { get; }
    public float Roughness { get; }
    public Vector3 Emissive { get; }
}
