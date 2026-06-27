using System.Numerics;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// A self-contained demo of <see cref="Engine3DView"/>: a lit, slowly-rotating cube and sphere over a
    /// ground plane. Proves the whole GPU pipeline (Vulkan offscreen render -> readback -> WriteableBitmap)
    /// end to end and is the reference for building real 3D controls. Drag to orbit, wheel to zoom.
    /// </summary>
    public sealed class Engine3DDemoView : Engine3DView
    {
        private readonly SceneNode _cube;
        private readonly SceneNode _sphere;
        private float _time;

        public Engine3DDemoView()
        {
            Scene.ClearColor = new Vector4(0.06f, 0.06f, 0.08f, 1f);
            Scene.Camera.Target = new Vector3(0f, 0.2f, 0f);
            Scene.Camera.Distance = 4.5f;

            Scene.Root.AddChild(new SceneNode
            {
                Name = "ground",
                Position = new Vector3(0f, -0.9f, 0f),
                Mesh = MeshData.Plane(6f),
                Material = new Material { BaseColor = new Vector4(0.15f, 0.16f, 0.20f, 1f), Roughness = 0.9f }
            });

            _cube = Scene.Root.AddChild(new SceneNode
            {
                Name = "cube",
                Mesh = MeshData.Box(1.2f),
                Material = new Material
                {
                    BaseColor = new Vector4(0.18f, 0.72f, 0.74f, 1f), // teal
                    Metallic = 0.35f,
                    Roughness = 0.35f,
                    Emissive = new Vector3(0.01f, 0.04f, 0.05f)
                }
            });

            _sphere = Scene.Root.AddChild(new SceneNode
            {
                Name = "sphere",
                Position = new Vector3(1.6f, 0.1f, -0.3f),
                Mesh = MeshData.Sphere(0.55f),
                Material = new Material
                {
                    BaseColor = new Vector4(0.80f, 0.55f, 0.95f, 1f), // mauve
                    Metallic = 0.1f,
                    Roughness = 0.5f
                }
            });

            OnUpdate = Animate;
        }

        private void Animate(Scene scene, double dt)
        {
            _time += (float)dt;
            _cube.Rotation = Quaternion.CreateFromYawPitchRoll(_time * 0.7f, _time * 0.4f, 0f);
            _sphere.Position = new Vector3(1.6f, 0.1f + 0.25f * System.MathF.Sin(_time * 1.5f), -0.3f);
        }
    }
}
