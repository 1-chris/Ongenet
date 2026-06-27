using System;
using System.Numerics;
using Avalonia.Media;
using Ongenet.App.Theming;
using Ongenet.Core.Audio.Effects;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls.Engine3D
{
    /// <summary>
    /// A 3D oscilloscope visualization: the live (smoothed, averaged) waveform is drawn as a glowing ribbon
    /// at the front, and periodic snapshots of it recede into the distance with fading opacity - a waterfall
    /// trail. Colours follow the Catppuccin theme (front = Mauve, trail = Sky) and update live. Reads its
    /// audio from an <see cref="IWaveformSource"/> (the pass-through 3D Scope effect).
    /// </summary>
    public sealed class WaveformTrailVisualization : IEngine3DVisualization
    {
        private const int Points = 160;            // waveform resolution
        private const int VertexCount = Points * 2; // top + bottom ribbon vertex per point
        private const int CaptureSamples = 2048;
        private const int SnapshotCount = 20;

        private const float SpawnInterval = 0.12f;  // seconds between trail snapshots
        private const float Lifetime = 2.2f;         // snapshot lifespan (seconds)
        private const float BaseAlpha = 0.5f;
        private const float ZSpawn = -0.25f;         // depth a snapshot is born at (just behind the front)
        private const float ZMax = 3.0f;             // depth it recedes to by end of life
        private const float RecedeSpeed = (ZMax - 0.25f) / Lifetime;
        private const float HalfWidth = 1.5f;
        private const float Amplitude = 0.95f;
        private const float HalfThickness = 0.018f;
        private const float Smoothing = 0.4f;

        private readonly IWaveformSource? _source;
        private readonly float[] _samples = new float[CaptureSamples];
        private readonly float[] _display = new float[Points];
        private uint[] _indices = Array.Empty<uint>();

        private SceneNode _front = null!;
        private MeshData _frontMesh = null!;
        private Material _frontMat = null!;

        private readonly SceneNode[] _snapNodes = new SceneNode[SnapshotCount];
        private readonly MeshData[] _snapMeshes = new MeshData[SnapshotCount];
        private readonly Material[] _snapMats = new Material[SnapshotCount];
        private readonly float[] _snapAge = new float[SnapshotCount];
        private readonly bool[] _snapActive = new bool[SnapshotCount];
        private float _sinceSpawn;

        private Vector3 _frontRgb = new(0.8f, 0.55f, 0.95f);
        private Vector3 _trailRgb = new(0.5f, 0.7f, 0.95f);

        public WaveformTrailVisualization(IWaveformSource? source) => _source = source;

        public void Build(Scene scene)
        {
            _indices = BuildIndices();

            scene.Camera.Target = new Vector3(0f, 0f, -1.1f);
            scene.Camera.Distance = 3.8f;
            scene.Camera.Yaw = 0.5f;
            scene.Camera.Pitch = 0.32f;

            _frontMesh = MeshData.CreateDynamic(VertexCount, _indices);
            _frontMesh.UpdateVertices(BuildVertices(_display));
            _frontMat = new Material { Metallic = 0.0f, Roughness = 0.4f };
            _front = scene.Root.AddChild(new SceneNode
            {
                Name = "waveform",
                Mesh = _frontMesh,
                Material = _frontMat,
                Position = new Vector3(0, 0, 0)
            });

            for (var k = 0; k < SnapshotCount; k++)
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

            // 1) Capture + average the latest audio into the display buffer, with temporal smoothing.
            var count = _source?.CaptureLatest(_samples) ?? 0;
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

            // 2) Rebuild the live front ribbon (fresh array -> safe ref-swap for the render thread).
            _frontMesh.UpdateVertices(BuildVertices(_display));

            // 3) Periodically spawn a snapshot of the current waveform behind the front.
            _sinceSpawn += dt;
            if (_sinceSpawn >= SpawnInterval)
            {
                _sinceSpawn = 0f;
                SpawnSnapshot();
            }

            // 4) Age the trail: recede in Z, fade out, recolour from the (possibly changed) theme.
            for (var k = 0; k < SnapshotCount; k++)
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
                var z = node.Position.Z - RecedeSpeed * dt;
                node.Position = new Vector3(0, 0, z);
                var fade = 1f - _snapAge[k] / Lifetime;
                _snapMats[k].BaseColor = new Vector4(_trailRgb, BaseAlpha * fade);
                _snapMats[k].Emissive = _trailRgb * (0.25f * fade);
            }
        }

        public void ApplyTheme(Scene scene)
        {
            _frontRgb = ToRgb(ThemePalette.Mauve);
            _trailRgb = ToRgb(ThemePalette.Sky);

            var bg = ToRgb(ThemePalette.Crust);
            scene.ClearColor = new Vector4(bg, 1f);

            if (_frontMat is not null)
            {
                _frontMat.BaseColor = new Vector4(_frontRgb, 1f);
                _frontMat.Emissive = _frontRgb * 0.7f;
            }
        }

        private void SpawnSnapshot()
        {
            var slot = -1;
            var oldest = -1f;
            for (var k = 0; k < SnapshotCount; k++)
            {
                if (!_snapActive[k]) { slot = k; break; }
                if (_snapAge[k] > oldest) { oldest = _snapAge[k]; slot = k; }
            }

            if (slot < 0) return;

            _snapMeshes[slot].UpdateVertices(BuildVertices(_display));
            _snapAge[slot] = 0f;
            _snapActive[slot] = true;
            var node = _snapNodes[slot];
            node.Visible = true;
            node.Position = new Vector3(0, 0, ZSpawn);
            _snapMats[slot].BaseColor = new Vector4(_trailRgb, BaseAlpha);
            _snapMats[slot].Emissive = _trailRgb * 0.25f;
        }

        private static Vertex[] BuildVertices(float[] display)
        {
            var verts = new Vertex[VertexCount];
            var normal = new Vector3(0, 0, 1);
            var white = new Vector4(1, 1, 1, 1);
            for (var i = 0; i < Points; i++)
            {
                var x = -HalfWidth + 2f * HalfWidth * i / (Points - 1);
                var y = Math.Clamp(display[i] * Amplitude, -1.3f, 1.3f);
                verts[2 * i] = new Vertex(new Vector3(x, y + HalfThickness, 0), normal, white);
                verts[2 * i + 1] = new Vertex(new Vector3(x, y - HalfThickness, 0), normal, white);
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

        private static Vector3 ToRgb(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
    }
}
