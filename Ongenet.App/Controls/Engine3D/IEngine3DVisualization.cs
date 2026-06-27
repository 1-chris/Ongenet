using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls.Engine3D
{
    /// <summary>
    /// A reusable, self-contained 3D visualization that drives an <see cref="Scene"/>. The generic
    /// <see cref="Engine3DVisualHost"/> owns the GPU view and calls these on the UI thread: <see cref="Build"/>
    /// once to populate the scene, <see cref="Update"/> every frame to animate it, and <see cref="ApplyTheme"/>
    /// on build and whenever the app theme changes so colours track Catppuccin live.
    ///
    /// Implementations should be cheap to construct (one per hosted view) and hold no GPU resources - they
    /// only mutate the portable scene model.
    /// </summary>
    public interface IEngine3DVisualization
    {
        /// <summary>One-time scene setup (nodes, meshes, camera).</summary>
        void Build(Scene scene);

        /// <summary>Per-frame animation/data update. <paramref name="dt"/> is seconds since the last frame.</summary>
        void Update(Scene scene, double dt);

        /// <summary>(Re)applies colours from the current theme palette. Called on build and on theme change.</summary>
        void ApplyTheme(Scene scene);
    }
}
