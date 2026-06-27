using System;
using Avalonia;
using Avalonia.Media;

namespace Ongenet.App.Controls.Engine3D
{
    /// <summary>
    /// Bridges a finished GPU frame onto the Avalonia render surface. Phase 1 is <see cref="ReadbackPresenter"/>
    /// (BGRA pixels into a WriteableBitmap - universal, composes perfectly with the UI). A future Phase 2
    /// CompositionInteropPresenter will import the GPU texture zero-copy where the platform supports it.
    /// </summary>
    internal interface IEngine3DPresenter : IDisposable
    {
        /// <summary>True once at least one frame has been presented (so the control can stop showing the placeholder).</summary>
        bool HasContent { get; }

        /// <summary>Updates the presenter's image from a finished CPU frame (UI thread).</summary>
        void Present(FrameBuffer frame);

        /// <summary>Draws the current image into <paramref name="bounds"/>.</summary>
        void Draw(DrawingContext context, Rect bounds);
    }
}
