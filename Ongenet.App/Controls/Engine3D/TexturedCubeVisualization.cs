using System;
using System.Numerics;
using Ongenet.Core.Models.Media;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls.Engine3D;

/// <summary>Rotating cube with user image baked onto faces for video 3D FX layers.</summary>
public sealed class TexturedCubeVisualization : IEngine3DVisualization
{
    private readonly VideoLayer _layer;
    private SceneNode _cube = null!;
    private string? _builtImagePath;
    private float _time;

    public TexturedCubeVisualization(VideoLayer layer) => _layer = layer;

    public void Build(Scene scene)
    {
        scene.Camera.Target = Vector3.Zero;
        scene.Camera.Distance = (float)_layer.Engine3DCameraDistance;
        scene.Camera.Yaw = (float)_layer.Engine3DCameraYaw;
        scene.Camera.Pitch = (float)_layer.Engine3DCameraPitch;

        _builtImagePath = _layer.Engine3DImagePath;
        _cube = scene.Root.AddChild(new SceneNode
        {
            Name = "textured-cube",
            Mesh = TexturedMeshBuilder.CreateTexturedBox(_builtImagePath, 1.4f),
            Material = new Material
            {
                BaseColor = Vector4.One,
                Metallic = 0.05f,
                Roughness = 0.85f
            }
        });
    }

    public void Update(Scene scene, double dt)
    {
        _time += (float)dt;
        _cube.Rotation = Quaternion.CreateFromYawPitchRoll(_time * 0.5f, _time * 0.3f, 0f);
        scene.Camera.Yaw = (float)_layer.Engine3DCameraYaw;
        scene.Camera.Pitch = (float)_layer.Engine3DCameraPitch;
        scene.Camera.Distance = (float)_layer.Engine3DCameraDistance;

        var path = _layer.Engine3DImagePath;
        if (!string.Equals(path, _builtImagePath, StringComparison.Ordinal))
        {
            _builtImagePath = path;
            _cube.Mesh = TexturedMeshBuilder.CreateTexturedBox(path, 1.4f);
        }
    }

    public void ApplyTheme(Scene scene) =>
        scene.TransparentBackground = _layer.Engine3DTransparentBackground;
}
