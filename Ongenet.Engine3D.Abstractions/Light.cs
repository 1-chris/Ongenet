using System.Numerics;

namespace Ongenet.Engine3D.Abstractions;

public enum LightKind
{
    /// <summary>A constant ambient term applied to every surface (<see cref="Light.Direction"/> ignored).</summary>
    Ambient,

    /// <summary>A directional (sun-like) light travelling along <see cref="Light.Direction"/>.</summary>
    Directional
}

/// <summary>A light in the scene. The reference renderer supports one ambient + one directional light.</summary>
public sealed class Light
{
    public LightKind Kind { get; set; } = LightKind.Directional;

    /// <summary>Direction the light travels (for <see cref="LightKind.Directional"/>); normalised by the renderer.</summary>
    public Vector3 Direction { get; set; } = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f));

    /// <summary>Linear RGB colour.</summary>
    public Vector3 Color { get; set; } = Vector3.One;

    /// <summary>Scalar brightness multiplier.</summary>
    public float Intensity { get; set; } = 1f;

    public static Light Directional(Vector3 direction, float intensity = 1f)
        => new() { Kind = LightKind.Directional, Direction = direction, Intensity = intensity };

    public static Light Ambient(float intensity = 0.2f)
        => new() { Kind = LightKind.Ambient, Intensity = intensity };
}
