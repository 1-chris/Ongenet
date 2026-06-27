using System;
using System.Threading;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls.Engine3D
{
    /// <summary>
    /// Drives an <see cref="I3DRenderSession"/> on a dedicated background thread so the GPU render + readback
    /// never block the UI thread. The UI thread only ever submits the latest scene + size and consumes
    /// finished frames; the session itself is touched only by this loop's thread (as it requires).
    ///
    /// Frames are triple-buffered: the render thread always has a free slot to write while the UI may be
    /// reading another and a third is the most-recently-published one, so neither side stalls the other.
    /// </summary>
    internal sealed class Engine3DRenderLoop : IDisposable
    {
        private readonly I3DRenderSession _session;
        private readonly Thread _thread;
        private readonly AutoResetEvent _signal = new(false);
        private readonly object _gate = new();
        private readonly FrameBuffer[] _slots = { new(), new(), new() };

        private SceneSnapshot? _pendingScene;
        private int _width = 1;
        private int _height = 1;

        private int _readyIndex = -1;   // a freshly published frame waiting to be consumed
        private int _readingIndex = -1; // the slot the UI is currently reading
        private volatile bool _running = true;

        public Engine3DRenderLoop(I3DRenderSession session)
        {
            _session = session;
            _thread = new Thread(Run) { IsBackground = true, Name = "Engine3D.Render" };
            _thread.Start();
        }

        /// <summary>Sets the latest scene + target pixel size and wakes the render thread.</summary>
        public void Submit(SceneSnapshot scene, int width, int height)
        {
            lock (_gate)
            {
                _pendingScene = scene;
                _width = Math.Max(1, width);
                _height = Math.Max(1, height);
            }

            _signal.Set();
        }

        /// <summary>Acquires the most recent finished frame, or null if none is new. Call <see cref="ReleaseFrame"/> after.</summary>
        public FrameBuffer? AcquireFrame()
        {
            lock (_gate)
            {
                if (_readyIndex < 0) return null;
                _readingIndex = _readyIndex;
                _readyIndex = -1;
                return _slots[_readingIndex];
            }
        }

        public void ReleaseFrame()
        {
            lock (_gate)
            {
                _readingIndex = -1;
            }
        }

        private void Run()
        {
            while (_running)
            {
                _signal.WaitOne(250);
                if (!_running) break;

                SceneSnapshot? scene;
                int w, h;
                lock (_gate)
                {
                    scene = _pendingScene;
                    w = _width;
                    h = _height;
                }

                if (scene is null || !_session.IsValid) continue;

                try
                {
                    _session.Resize(w, h);
                    _session.Submit(scene);
                    var frame = _session.RenderFrame();
                    if (frame.Kind == FramePresentKind.Cpu && frame.Pixels is { } pixels)
                        Publish(pixels, frame.Width, frame.Height, frame.Stride);
                }
                catch
                {
                    // A device error flips the session invalid; the loop idles and the control shows fallback.
                }
            }
        }

        // Copies the session's pixels into a free slot (not the one being read, not the last published) and
        // publishes it. The copy happens on the render thread while the session buffer is still valid.
        private void Publish(byte[] pixels, int width, int height, int stride)
        {
            int slot;
            lock (_gate)
            {
                slot = 0;
                while (slot < _slots.Length && (slot == _readingIndex || slot == _readyIndex)) slot++;
                if (slot >= _slots.Length) slot = 0;
            }

            var fb = _slots[slot];
            var bytes = height * stride;
            fb.EnsureCapacity(bytes);
            Array.Copy(pixels, fb.Pixels, Math.Min(bytes, pixels.Length));
            fb.Width = width;
            fb.Height = height;
            fb.Stride = stride;

            lock (_gate)
            {
                _readyIndex = slot;
            }
        }

        public void Dispose()
        {
            _running = false;
            _signal.Set();
            _thread.Join(1000);
            _signal.Dispose();
            _session.Dispose();
        }
    }
}
