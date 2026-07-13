using System;
using System.Numerics;
using Avalonia.Media;
using Ongenet.App.Theming;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls.Engine3D;

/// <summary>3D oscilloscope for video waveform layers, fed by <see cref="IVideoAudioScopeService"/>.</summary>
public sealed class VideoScope3DVisualization : IEngine3DVisualization
{
    private const int Points = 160;
    private const int VertexCount = Points * 2;
    private const int CaptureSamples = 2048;
    private const float SpawnInterval = 0.12f;
    private const float Lifetime = 2.2f;
    private const float BaseAlpha = 0.5f;
    private const float ZSpawn = -0.25f;
    private const float ZMax = 3.0f;
    private const float RecedeSpeed = (ZMax - 0.25f) / Lifetime;
    private const float HalfWidth = 1.5f;
    private const float Amplitude = 0.95f;
    private const float Smoothing = 0.4f;

    private readonly Guid _trackId;
    private readonly IVideoAudioScopeService _scope;
    private readonly VideoLayer _layer;
    private readonly float[] _samples = new float[CaptureSamples];
    private readonly float[] _display = new float[Points];
    private uint[] _indices = Array.Empty<uint>();

    private SceneNode _front = null!;
    private MeshData _frontMesh = null!;
    private Material _frontMat = null!;
    private SceneNode[] _snapNodes = Array.Empty<SceneNode>();
    private MeshData[] _snapMeshes = Array.Empty<MeshData>();
    private Material[] _snapMats = Array.Empty<Material>();
    private float[] _snapAge = Array.Empty<float>();
    private bool[] _snapActive = Array.Empty<bool>();
    private float _sinceSpawn;
    private Vector3 _frontRgb = new(0.8f, 0.55f, 0.95f);
    private Vector3 _trailRgb = new(0.5f, 0.7f, 0.95f);
    private int _trailCount = 20;
    private float _halfThickness = 0.018f;

    public VideoScope3DVisualization(Guid trackId, IVideoAudioScopeService scope, VideoLayer layer)
    {
        _trackId = trackId;
        _scope = scope;
        _layer = layer;
        _trailCount = Math.Clamp(layer.Scope3DTrailCount, 4, 40);
        _halfThickness = (float)Math.Clamp(layer.Scope3DLineThickness, 0.005, 0.08);
    }

    public void Build(Scene scene)
    {
        _indices = BuildIndices();
        _snapNodes = new SceneNode[_trailCount];
        _snapMeshes = new MeshData[_trailCount];
        _snapMats = new Material[_trailCount];
        _snapAge = new float[_trailCount];
        _snapActive = new bool[_trailCount];

        scene.Camera.Target = new Vector3(0f, 0f, -1.1f);
        scene.Camera.Distance = (float)_layer.Scope3DCameraDistance;
        scene.Camera.Yaw = (float)_layer.Scope3DCameraYaw;
        scene.Camera.Pitch = (float)_layer.Scope3DCameraPitch;

        _frontMesh = MeshData.CreateDynamic(VertexCount, _indices);
        _frontMesh.UpdateVertices(BuildVertices(_display));
        _frontMat = new Material { Metallic = 0f, Roughness = 0.4f };
        _front = scene.Root.AddChild(new SceneNode
        {
            Name = "waveform",
            Mesh = _frontMesh,
            Material = _frontMat
        });

        for (var k = 0; k < _trailCount; k++)
        {
            _snapMeshes[k] = MeshData.CreateDynamic(VertexCount, _indices);
            _snapMats[k] = new Material { Metallic = 0f, Roughness = 0.6f };
            _snapNodes[k] = scene.Root.AddChild(new SceneNode
            {
                Name = $"trail{k}",
                Mesh = _snapMeshes[k],
                Material = _snapMats[k],
                Visible = false,
                Position = new Vector3(0, 0, ZSpawn)
            });
        }
    }

    public void Update(Scene scene, double dtSeconds)
    {
        var dt = (float)dtSeconds;
        _halfThickness = (float)Math.Clamp(_layer.Scope3DLineThickness, 0.005, 0.08);

        var count = _scope.CaptureLatest(_trackId, _samples);
        if (count > 0)
        {
            var bucket = (float)count / Points;
            for (var i = 0; i < Points; i++)
            {
                var start = (int)(i * bucket);
                var end = (int)((i + 1) * bucket);
                if (end <= start) end = start + 1;
                if (end > count) end = count;
                float sum = 0;
                for (var s = start; s < end; s++) sum += _samples[s];
                var target = sum / Math.Max(1, end - start);
                _display[i] += (target - _display[i]) * Smoothing;
            }
        }
        else
        {
            for (var i = 0; i < Points; i++) _display[i] += (0f - _display[i]) * Smoothing;
        }

        _frontMesh.UpdateVertices(BuildVertices(_display));

        _sinceSpawn += dt;
        if (_sinceSpawn >= SpawnInterval)
        {
            _sinceSpawn = 0f;
            SpawnSnapshot();
        }

        for (var k = 0; k < _trailCount; k++)
        {
            if (!_snapActive[k]) continue;
            _snapAge[k] += dt;
            if (_snapAge[k] >= Lifetime)
            {
                _snapActive[k] = false;
                _snapNodes[k].Visible = false;
                continue;
            }

            var node = _snapNodes[k];
            node.Position = new Vector3(0, 0, node.Position.Z - RecedeSpeed * dt);
            var fade = 1f - _snapAge[k] / Lifetime;
            _snapMats[k].BaseColor = new Vector4(_trailRgb, BaseAlpha * fade);
            _snapMats[k].Emissive = _trailRgb * (0.25f * fade);
        }
    }

    public void ApplyTheme(Scene scene)
    {
        var frontColor = ArgbToColor(_layer.WaveformColorArgb);
        var secondary = ArgbToColor(_layer.VisualiserColorSecondaryArgb);
        _frontRgb = ToRgb(frontColor);
        _trailRgb = ToRgb(secondary);

        scene.TransparentBackground = _layer.Scope3DTransparentBackground;
        if (!scene.TransparentBackground)
        {
            var bg = ToRgb(ThemePalette.Crust);
            scene.ClearColor = new Vector4(bg, 1f);
        }

        _frontMat.BaseColor = new Vector4(_frontRgb, 1f);
        _frontMat.Emissive = _frontRgb * 0.7f;
    }

    private void SpawnSnapshot()
    {
        var slot = -1;
        var oldest = -1f;
        for (var k = 0; k < _trailCount; k++)
        {
            if (!_snapActive[k]) { slot = k; break; }
            if (_snapAge[k] > oldest) { oldest = _snapAge[k]; slot = k; }
        }

        if (slot < 0) return;

        _snapMeshes[slot].UpdateVertices(BuildVertices(_display));
        _snapAge[slot] = 0f;
        _snapActive[slot] = true;
        _snapNodes[slot].Visible = true;
        _snapNodes[slot].Position = new Vector3(0, 0, ZSpawn);
        _snapMats[slot].BaseColor = new Vector4(_trailRgb, BaseAlpha);
        _snapMats[slot].Emissive = _trailRgb * 0.25f;
    }

    private Vertex[] BuildVertices(float[] display)
    {
        var verts = new Vertex[VertexCount];
        var normal = new Vector3(0, 0, 1);
        var white = new Vector4(1, 1, 1, 1);
        for (var i = 0; i < Points; i++)
        {
            var x = -HalfWidth + 2f * HalfWidth * i / (Points - 1);
            var y = Math.Clamp(display[i] * Amplitude, -1.3f, 1.3f);
            verts[2 * i] = new Vertex(new Vector3(x, y + _halfThickness, 0), normal, white);
            verts[2 * i + 1] = new Vertex(new Vector3(x, y - _halfThickness, 0), normal, white);
        }

        return verts;
    }

    private static uint[] BuildIndices()
    {
        var idx = new uint[(Points - 1) * 6];
        var o = 0;
        for (var i = 0; i < Points - 1; i++)
        {
            uint topA = (uint)(2 * i), botA = (uint)(2 * i + 1);
            uint topB = (uint)(2 * (i + 1)), botB = (uint)(2 * (i + 1) + 1);
            idx[o++] = topA; idx[o++] = topB; idx[o++] = botB;
            idx[o++] = topA; idx[o++] = botB; idx[o++] = botA;
        }

        return idx;
    }

    private static Color ArgbToColor(uint argb) => Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    private static Vector3 ToRgb(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
}
