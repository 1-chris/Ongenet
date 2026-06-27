using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;

namespace Ongenet.App.Controls.Engine3D
{
    /// <summary>
    /// Phase 2 zero-copy presenter: composites a GPU texture the engine shares with the compositor, via a
    /// <see cref="CompositionDrawingSurface"/> attached as the host element's child visual (so it layers
    /// correctly with the rest of the UI - no airspace). The present-side wiring lives here; the matching
    /// half is the Vulkan backend exporting an external-memory image + sync primitives, which flips a
    /// session to <see cref="Engine3D.Abstractions.FramePresentKind.SharedHandle"/>. Until that export lands,
    /// <see cref="Engine3DInterop.CreatePresenter"/> selects the readback presenter instead, so this is never
    /// constructed on a CPU-frame session.
    /// </summary>
    internal sealed class CompositionInteropPresenter : IEngine3DPresenter
    {
        private readonly Visual _host;
        private readonly CompositionDrawingSurface? _surface;
        private CompositionSurfaceVisual? _surfaceVisual;

        public CompositionInteropPresenter(Visual host)
        {
            _host = host;
            var elementVisual = ElementComposition.GetElementVisual(host);
            var compositor = elementVisual?.Compositor;
            if (compositor is null) return;

            _surface = compositor.CreateDrawingSurface();
            _surfaceVisual = compositor.CreateSurfaceVisual();
            _surfaceVisual.Surface = _surface;
            ElementComposition.SetElementChildVisual(host, _surfaceVisual);
        }

        // The compositor draws the surface visual itself; report content so the control doesn't paint a
        // placeholder over it.
        public bool HasContent => _surface is not null;

        public void Present(FrameBuffer frame)
        {
            // CPU frames are not used on this path - shared-handle frames are imported via UpdateShared once
            // the Vulkan export half is implemented. Keep the surface visual sized to the frame.
            if (_surfaceVisual is not null && frame.Width > 0 && frame.Height > 0)
                _surfaceVisual.Size = new Vector2(frame.Width, frame.Height);
        }

        public void Draw(DrawingContext context, Rect bounds)
        {
            // No-op: the attached composition surface visual is composited independently of the draw pass.
        }

        public void Dispose()
        {
            ElementComposition.SetElementChildVisual(_host, null!);
            _surfaceVisual = null;
        }
    }
}
