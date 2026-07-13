using System;
using System.Numerics;
using Avalonia.Media;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls.Engine3D;

/// <summary>Audio-reactive particle field for video 3D FX layers.</summary>
public sealed class AudioParticleVisualization : IEngine3DVisualization
{
    private const int CaptureSamples = 2048;
    private const int BandCount = 32;
    private const int MaxParticles = 384;

    private readonly VideoLayer _layer;
    private readonly IVideoAudioScopeService _scope;
    private readonly float[] _samples = new float[CaptureSamples];
    private readonly float[] _bands = new float[BandCount];
    private readonly SceneNode[] _particles = new SceneNode[MaxParticles];
    private readonly float[] _seeds = new float[MaxParticles];
    private readonly Material[] _materials = new Material[MaxParticles];
    private float _time;
    private VideoEngine3DParticleShape _shape = (VideoEngine3DParticleShape)(-1);

    public AudioParticleVisualization(VideoLayer layer, IVideoAudioScopeService scope)
    {
        _layer = layer;
        _scope = scope;
        var rng = new Random(42);
        for (var i = 0; i < MaxParticles; i++)
            _seeds[i] = (float)rng.NextDouble();
    }

    public void Build(Scene scene)
    {
        scene.Camera.Target = new Vector3(0, 0, -0.6f);
        scene.Camera.Distance = (float)_layer.Engine3DCameraDistance;
        scene.Camera.Yaw = (float)_layer.Engine3DCameraYaw;
        scene.Camera.Pitch = (float)_layer.Engine3DCameraPitch;

        var mesh = GetShapeMesh(_layer.Engine3DParticleShape);
        _shape = _layer.Engine3DParticleShape;
        var active = ActiveCount();
        for (var i = 0; i < MaxParticles; i++)
        {
            _materials[i] = new Material { Metallic = 0.05f, Roughness = 0.35f };
            _particles[i] = scene.Root.AddChild(new SceneNode
            {
                Name = $"particle{i}",
                Mesh = mesh,
                Material = _materials[i],
                Position = RestPosition(i),
                Visible = i < active
            });
        }
    }

    public void Update(Scene scene, double dt)
    {
        _time += (float)dt;
        var active = ActiveCount();
        scene.Camera.Yaw = (float)_layer.Engine3DCameraYaw;
        scene.Camera.Pitch = (float)_layer.Engine3DCameraPitch;
        scene.Camera.Distance = (float)_layer.Engine3DCameraDistance;

        if (_layer.Engine3DParticleShape != _shape)
        {
            _shape = _layer.Engine3DParticleShape;
            var mesh = GetShapeMesh(_shape);
            for (var i = 0; i < MaxParticles; i++)
                _particles[i].Mesh = mesh;
        }

        ComputeBands();
        var rgb = ArgbToRgb(_layer.Engine3DParticleColorArgb);
        var baseSize = (float)Math.Clamp(_layer.Engine3DParticleSize, 0.01, 0.35);
        var billboard = Quaternion.CreateFromYawPitchRoll(scene.Camera.Yaw, scene.Camera.Pitch, 0f);

        for (var i = 0; i < MaxParticles; i++)
        {
            var node = _particles[i];
            if (i >= active)
            {
                node.Visible = false;
                continue;
            }

            node.Visible = true;
            var band = _bands[i % BandCount];
            var pulse = 0.65f + 0.35f * MathF.Sin(_time * 3.5f + _seeds[i] * 12f);
            var energy = Math.Clamp(band * pulse, 0.05f, 1f);
            var spread = 2.8f;
            var x = (_seeds[i] - 0.5f) * spread;
            var y = (energy - 0.35f) * 1.8f + MathF.Sin(_time * 2f + _seeds[i] * 18f) * 0.12f;
            var z = -1.2f - _seeds[i] * 1.6f - energy * 0.35f;
            node.Position = new Vector3(x, y, z);
            node.Rotation = billboard;
            node.Scale = Vector3.One * (baseSize * (0.55f + energy * 1.1f) * ShapeScale(_shape));

            var alpha = Math.Clamp(0.25f + energy * 0.75f, 0.15f, 1f);
            _materials[i].BaseColor = new Vector4(rgb, alpha);
            _materials[i].Emissive = rgb * (0.35f + energy * 0.85f);
        }
    }

    public void ApplyTheme(Scene scene) =>
        scene.TransparentBackground = _layer.Engine3DTransparentBackground;

    private int ActiveCount() =>
        Math.Clamp(_layer.Engine3DParticleCount, 16, MaxParticles);

    private void ComputeBands()
    {
        Array.Clear(_bands, 0, _bands.Length);
        if (_layer.Engine3DAudioSourceTrackId is not { } trackId)
            return;

        var n = _scope.CaptureLatest(trackId, _samples);
        if (n <= 0) return;

        for (var i = 0; i < n; i++)
        {
            var band = (int)((long)i * BandCount / n);
            if (band >= BandCount) band = BandCount - 1;
            _bands[band] += Math.Abs(_samples[i]);
        }

        var max = 0f;
        for (var b = 0; b < BandCount; b++)
        {
            var count = Math.Max(1, n / BandCount);
            _bands[b] /= count;
            max = Math.Max(max, _bands[b]);
        }

        if (max > 1e-6f)
        {
            for (var b = 0; b < BandCount; b++)
                _bands[b] = Math.Clamp(_bands[b] / max, 0f, 1f);
        }
    }

    private Vector3 RestPosition(int i)
    {
        var s = _seeds[i];
        return new Vector3((s - 0.5f) * 2.8f, 0, -1.2f - s * 1.6f);
    }

    private static float ShapeScale(VideoEngine3DParticleShape shape) => shape switch
    {
        VideoEngine3DParticleShape.Point => 0.45f,
        VideoEngine3DParticleShape.Quad => 1.1f,
        _ => 1f
    };

    private static MeshData GetShapeMesh(VideoEngine3DParticleShape shape) => shape switch
    {
        VideoEngine3DParticleShape.Quad => QuadMesh,
        VideoEngine3DParticleShape.Point => PointMesh,
        _ => DiscMesh
    };

    private static readonly MeshData DiscMesh = MeshData.Sphere(0.5f, 8);
    private static readonly MeshData PointMesh = MeshData.Sphere(0.5f, 6);
    private static readonly MeshData QuadMesh = CreateQuadMesh();

    private static MeshData CreateQuadMesh()
    {
        var n = new Vector3(0, 0, 1);
        var c = new Vector4(1, 1, 1, 1);
        var verts = new[]
        {
            new Vertex(new Vector3(-0.5f, -0.5f, 0), n, c),
            new Vertex(new Vector3(0.5f, -0.5f, 0), n, c),
            new Vertex(new Vector3(0.5f, 0.5f, 0), n, c),
            new Vertex(new Vector3(-0.5f, 0.5f, 0), n, c)
        };
        return new MeshData(verts, [0, 1, 2, 0, 2, 3]);
    }

    private static Vector3 ArgbToRgb(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f,
        ((argb >> 8) & 0xFF) / 255f,
        (argb & 0xFF) / 255f);
}
