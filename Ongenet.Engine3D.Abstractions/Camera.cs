using System;
using System.Numerics;

namespace Ongenet.Engine3D.Abstractions;

public enum CameraProjection
{
    Perspective,
    Orthographic
}

/// <summary>
/// An orbit camera: it looks at <see cref="Target"/> from a distance, rotated by <see cref="Yaw"/> and
/// <see cref="Pitch"/>. This is the natural model for inspecting a 3D control - drag to orbit, wheel to
/// zoom. View/projection matrices are produced in the standard right-handed, [0,1] depth convention
/// (DirectX-style); the Vulkan backend applies the Y-flip it needs at present time.
/// </summary>
public sealed class Camera
{
    public CameraProjection Projection { get; set; } = CameraProjection.Perspective;

    /// <summary>Point the camera orbits and looks at.</summary>
    public Vector3 Target { get; set; } = Vector3.Zero;

    /// <summary>Distance from <see cref="Target"/> to the camera.</summary>
    public float Distance { get; set; } = 4f;

    /// <summary>Horizontal orbit angle, radians.</summary>
    public float Yaw { get; set; } = 0.6f;

    /// <summary>Vertical orbit angle, radians (clamped away from the poles by <see cref="Orbit"/>).</summary>
    public float Pitch { get; set; } = 0.45f;

    /// <summary>Vertical field of view, radians (perspective only).</summary>
    public float FieldOfView { get; set; } = MathF.PI / 4f;

    /// <summary>Half-height of the orthographic view volume in world units (orthographic only).</summary>
    public float OrthographicHalfHeight { get; set; } = 2f;

    public float NearPlane { get; set; } = 0.05f;
    public float FarPlane { get; set; } = 100f;

    /// <summary>The world-space camera position derived from the orbit parameters.</summary>
    public Vector3 Position
    {
        get
        {
            var cp = MathF.Cos(Pitch);
            var dir = new Vector3(cp * MathF.Sin(Yaw), MathF.Sin(Pitch), cp * MathF.Cos(Yaw));
            return Target + dir * Distance;
        }
    }

    /// <summary>Applies an orbit drag (radians) and a zoom factor, clamping pitch and distance.</summary>
    public void Orbit(float deltaYaw, float deltaPitch, float zoomFactor = 1f)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -1.5f, 1.5f);
        if (zoomFactor != 1f) Distance = Math.Clamp(Distance * zoomFactor, 0.2f, 1000f);
    }

    public Matrix4x4 ViewMatrix() => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);

    public Matrix4x4 ProjectionMatrix(float aspect)
    {
        if (aspect <= 0) aspect = 1f;
        if (Projection == CameraProjection.Orthographic)
        {
            var halfH = MathF.Max(0.01f, OrthographicHalfHeight);
            return Matrix4x4.CreateOrthographic(halfH * 2f * aspect, halfH * 2f, NearPlane, FarPlane);
        }

        var fov = Math.Clamp(FieldOfView, 0.05f, MathF.PI - 0.05f);
        return Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, NearPlane, FarPlane);
    }
}
