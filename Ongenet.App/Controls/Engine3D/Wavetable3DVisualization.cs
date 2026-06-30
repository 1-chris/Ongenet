using System;
using System.Numerics;
using Avalonia.Media;
using Ongenet.App.Theming;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls.Engine3D
{
    /// <summary>
    /// A 3D view of a <see cref="WavetableInstrument"/>'s table: each frame is drawn as a ribbon, stacked in
    /// depth and viewed at an angle, so you see the waveform morph through the table. The frame nearest the
    /// live scan position glows (cycling as the LFO/position sweeps). Frames are dynamic meshes rebuilt only
    /// when the table changes (load sample / preset). Colours follow the Catppuccin theme.
    /// </summary>
    public sealed class Wavetable3DVisualization : IEngine3DVisualization
    {
        private const int DisplayFrames = 48;
        private const int Samples = 128;
        private const int VertexCount = Samples * 2;
        private const float Spacing = 0.13f;
        private const float HalfWidth = 1.25f;
        private const float Amplitude = 0.7f;
        private const float HalfThickness = 0.012f;
        private const float HighlightFalloff = 6f; // frames over which the glow fades

        private readonly WavetableInstrument _inst;
        private uint[] _indices = Array.Empty<uint>();
        private readonly SceneNode[] _nodes = new SceneNode[DisplayFrames];
        private readonly MeshData[] _meshes = new MeshData[DisplayFrames];
        private readonly Material[] _mats = new Material[DisplayFrames];
        private int _lastRevision = -1;

        // The "scan cursor": a bright ribbon showing the exact, continuously-morphed current waveform that
        // glides through the stack so you can see the scan position move (driven by the Position knob + LFO).
        private SceneNode _cursor = null!;
        private MeshData _cursorMesh = null!;
        private Material _cursorMat = null!;
        private float _smoothPos = -1f;

        private Vector3 _accent = new(0.8f, 0.55f, 0.95f);
        private Vector3 _cursorRgb = new(0.95f, 0.7f, 1f);
        private Vector3 _dim = new(0.25f, 0.30f, 0.5f);

        public Wavetable3DVisualization(WavetableInstrument instrument) => _inst = instrument;

        public void Build(Scene scene)
        {
            _indices = BuildIndices();

            scene.Camera.Target = new Vector3(0f, 0f, -(DisplayFrames - 1) * Spacing * 0.5f);
            scene.Camera.Distance = 5.4f;
            scene.Camera.Yaw = 0.62f;
            scene.Camera.Pitch = 0.42f;

            for (var d = 0; d < DisplayFrames; d++)
            {
                _meshes[d] = MeshData.CreateDynamic(VertexCount, _indices);
                _mats[d] = new Material { Metallic = 0.1f, Roughness = 0.5f };
                _nodes[d] = scene.Root.AddChild(new SceneNode
                {
                    Name = $"frame{d}",
                    Mesh = _meshes[d],
                    Material = _mats[d],
                    Position = new Vector3(0, 0, -d * Spacing)
                });
            }

            // Cursor on top (added last → drawn over the stack), thicker + emissive.
            _cursorMesh = MeshData.CreateDynamic(VertexCount, _indices);
            _cursorMat = new Material { Metallic = 0.2f, Roughness = 0.3f };
            _cursor = scene.Root.AddChild(new SceneNode { Name = "scan-cursor", Mesh = _cursorMesh, Material = _cursorMat });

            RebuildGeometry();
        }

        public void Update(Scene scene, double dt)
        {
            if (_inst.TableRevision != _lastRevision) RebuildGeometry();

            var target = Math.Clamp(_inst.DisplayPosition, 0f, 1f);
            if (_smoothPos < 0f) _smoothPos = target;            // first frame: snap
            else _smoothPos += (target - _smoothPos) * 0.25f;    // glide toward the live position

            // Context stack: a gentle glow that brightens near the cursor (extra depth cue).
            var cur = _smoothPos * (DisplayFrames - 1);
            for (var d = 0; d < DisplayFrames; d++)
            {
                var prox = Math.Max(0f, 1f - Math.Abs(d - cur) / HighlightFalloff);
                prox *= prox;
                var rgb = Vector3.Lerp(_dim, _accent, prox * 0.6f);
                _mats[d].BaseColor = new Vector4(rgb, 1f);
                _mats[d].Emissive = _accent * (0.18f * prox) + _dim * 0.03f;
            }

            // Cursor: redraw the live morphed waveform at the (smoothed) position, glide it to the right Z,
            // nudged toward the camera so it reads as the "playhead" sweeping through the table.
            var table = _inst.Table;
            _cursorMesh.UpdateVertices(BuildCursorVertices(table, _smoothPos));
            var z = -cur * Spacing + Spacing * 0.5f;
            _cursor.Position = new Vector3(0, 0, z);
            _cursorMat.BaseColor = new Vector4(_cursorRgb, 1f);
            _cursorMat.Emissive = _cursorRgb * 0.9f;
        }

        public void ApplyTheme(Scene scene)
        {
            _accent = ToRgb(ThemePalette.Mauve);
            _cursorRgb = ToRgb(ThemePalette.Pink);
            _dim = ToRgb(ThemePalette.Blue) * 0.4f;
            scene.ClearColor = new Vector4(ToRgb(ThemePalette.Crust), 1f);
        }

        private void RebuildGeometry()
        {
            var table = _inst.Table;
            _lastRevision = _inst.TableRevision;
            for (var d = 0; d < DisplayFrames; d++)
                _meshes[d].UpdateVertices(BuildFrameVertices(table, d));
        }

        // The continuously-morphed current waveform (between frames), drawn slightly taller + thicker so the
        // moving cursor stands out from the static stack.
        private static Vertex[] BuildCursorVertices(Wavetable table, float position)
        {
            const float cursorAmp = Amplitude * 1.12f;
            const float cursorThick = HalfThickness * 2.4f;
            var verts = new Vertex[VertexCount];
            var normal = new Vector3(0, 0, 1);
            var white = new Vector4(1, 1, 1, 1);
            for (var j = 0; j < Samples; j++)
            {
                var x = -HalfWidth + 2f * HalfWidth * j / (Samples - 1);
                var phase = (float)j / Samples;
                var y = table.Read(position, phase) * cursorAmp;
                verts[2 * j] = new Vertex(new Vector3(x, y + cursorThick, 0), normal, white);
                verts[2 * j + 1] = new Vertex(new Vector3(x, y - cursorThick, 0), normal, white);
            }

            return verts;
        }

        private static Vertex[] BuildFrameVertices(Wavetable table, int displayFrame)
        {
            var tableFrame = DisplayFrames > 1
                ? (int)MathF.Round((float)displayFrame / (DisplayFrames - 1) * (table.FrameCount - 1))
                : 0;
            var verts = new Vertex[VertexCount];
            var normal = new Vector3(0, 0, 1);
            var white = new Vector4(1, 1, 1, 1);
            for (var j = 0; j < Samples; j++)
            {
                var x = -HalfWidth + 2f * HalfWidth * j / (Samples - 1);
                var idx = (int)((long)j * table.FrameLength / Samples);
                var y = table.Sample(tableFrame, idx) * Amplitude;
                verts[2 * j] = new Vertex(new Vector3(x, y + HalfThickness, 0), normal, white);
                verts[2 * j + 1] = new Vertex(new Vector3(x, y - HalfThickness, 0), normal, white);
            }

            return verts;
        }

        private static uint[] BuildIndices()
        {
            var idx = new uint[(Samples - 1) * 6];
            var o = 0;
            for (var i = 0; i < Samples - 1; i++)
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
