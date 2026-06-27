using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Ongenet.App.Controls.Engine3D
{
    /// <summary>
    /// Phase 1 presenter: copies the engine's BGRA readback pixels into a <see cref="WriteableBitmap"/> and
    /// draws it. Works on every platform and composes correctly with the rest of the UI (z-order, opacity,
    /// clipping, transforms) because it is just a bitmap - no native child surface, no airspace issues.
    /// </summary>
    internal sealed class ReadbackPresenter : IEngine3DPresenter
    {
        private WriteableBitmap? _bitmap;
        private int _width;
        private int _height;

        public bool HasContent => _bitmap is not null;

        public void Present(FrameBuffer frame)
        {
            if (frame.Width <= 0 || frame.Height <= 0) return;
            EnsureBitmap(frame.Width, frame.Height);

            using var locked = _bitmap!.Lock();
            var rowBytes = locked.RowBytes;
            var copyBytes = Math.Min(frame.Stride, rowBytes);
            for (var y = 0; y < frame.Height; y++)
            {
                var srcOffset = y * frame.Stride;
                if (srcOffset + copyBytes > frame.Pixels.Length) break;
                Marshal.Copy(frame.Pixels, srcOffset, locked.Address + y * rowBytes, copyBytes);
            }
        }

        public void Draw(DrawingContext context, Rect bounds)
        {
            if (_bitmap is null) return;
            var src = new Rect(0, 0, _width, _height);
            context.DrawImage(_bitmap, src, bounds);
        }

        private void EnsureBitmap(int width, int height)
        {
            if (_bitmap is not null && _width == width && _height == height) return;
            _bitmap?.Dispose();
            _width = width;
            _height = height;
            _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
        }

        public void Dispose()
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}
