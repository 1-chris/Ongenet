using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Controls.Engine3D;
using Ongenet.App.Services;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// An embeddable, GPU-rendered 3D viewport. It owns a mutable <see cref="Scene"/> the app builds/animates,
    /// resolves the native engine (<see cref="I3DEngineFactory"/>) from DI at runtime, and renders on a
    /// background thread, presenting finished frames via the readback presenter. When no engine is available
    /// (Browser/Android, or no usable GPU) it degrades to a quiet placeholder, so it is safe to place in any
    /// shared view. Drag to orbit, wheel to zoom.
    /// </summary>
    public class Engine3DView : Control
    {
        private I3DEngineFactory? _factory;
        private Engine3DRenderLoop? _loop;
        private IEngine3DPresenter? _presenter;
        private FrameTicker? _ticker;
        private Engine3DInterop.Capabilities _interopCaps;
        private bool _interopProbed;

        private bool _attached;
        private bool _initTried;
        private bool _engineFailed;

        private long _lastTickMs;
        private bool _dragging;
        private Point _lastPointer;

        private IBrush _backBrush = new SolidColorBrush(Color.FromRgb(18, 18, 23));
        private IBrush _textBrush = new SolidColorBrush(Color.FromRgb(120, 120, 135));

        /// <summary>The editable scene. Build your control's geometry under <see cref="Scene.Root"/>.</summary>
        public Scene Scene { get; } = new();

        /// <summary>
        /// Optional per-frame update hook (scene, elapsed seconds since the last frame), called on the UI
        /// thread before the scene is snapshotted - the place to animate nodes, react to audio, etc.
        /// </summary>
        public Action<Scene, double>? OnUpdate { get; set; }

        /// <summary>True when a GPU engine is available and a session was created.</summary>
        public bool IsEngineAvailable => _loop is not null;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            _lastTickMs = Environment.TickCount64;
            _ticker ??= new FrameTicker(this, OnTick);
            _ticker.SetFast(true);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _attached = false;
            _ticker?.SetFast(false);
            _loop?.Dispose();
            _loop = null;
            _presenter?.Dispose();
            _presenter = null;
            _initTried = false;
            _engineFailed = false;
        }

        private (int Width, int Height) PixelSize()
        {
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            var w = (int)Math.Round(Bounds.Width * scaling);
            var h = (int)Math.Round(Bounds.Height * scaling);
            return (Math.Max(0, w), Math.Max(0, h));
        }

        private void OnTick()
        {
            if (!_attached) return;
            var (pw, ph) = PixelSize();
            if (pw < 1 || ph < 1) return;

            var now = Environment.TickCount64;
            var dt = Math.Clamp((now - _lastTickMs) / 1000.0, 0.0, 0.1);
            _lastTickMs = now;

            EnsureEngine(pw, ph);
            OnUpdate?.Invoke(Scene, dt);

            if (_loop is null)
            {
                InvalidateVisual(); // keep the placeholder painted
                return;
            }

            var snapshot = SceneSnapshot.Capture(Scene);
            _loop.Submit(snapshot, pw, ph);

            var frame = _loop.AcquireFrame();
            if (frame is not null)
            {
                try { _presenter!.Present(frame); }
                finally { _loop.ReleaseFrame(); }
                InvalidateVisual();
            }
        }

        private void EnsureEngine(int width, int height)
        {
            if (_loop is not null || _engineFailed) return;
            if (!_initTried)
            {
                _initTried = true;
                _factory = App.ServiceProvider?.GetService<I3DEngineFactory>();
            }

            if (_factory is null || !_factory.IsAvailable) { _engineFailed = true; return; }

            var session = _factory.CreateSession(width, height);
            if (session is null) { _engineFailed = true; return; }

            // Pick the presenter for how this session delivers frames. Today that's CPU readback; the probe
            // also records whether zero-copy compositor interop would be available for a future shared-handle
            // session (see Engine3DInterop). The probe is async and best-effort, so it never blocks startup.
            if (!_interopProbed)
            {
                _interopProbed = true;
                _ = ProbeInteropAsync();
            }

            _presenter = Engine3DInterop.CreatePresenter(session, this, _interopCaps);
            _loop = new Engine3DRenderLoop(session);
        }

        private async System.Threading.Tasks.Task ProbeInteropAsync()
        {
            try { _interopCaps = await Engine3DInterop.ProbeAsync(this); }
            catch { /* diagnostics only */ }
        }

        public sealed override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width < 1 || bounds.Height < 1) return;

            if (_presenter is { HasContent: true })
            {
                _presenter.Draw(context, bounds);
                return;
            }

            // Placeholder: a quiet panel, with a hint when no GPU engine is available.
            context.DrawRectangle(_backBrush, null, bounds);
            if (_engineFailed)
            {
                var text = new FormattedText("3D not available on this device",
                    System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 12, _textBrush);
                context.DrawText(text, new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2));
            }
        }

        // ---------------------------------------------------------------- orbit input

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true;
            _lastPointer = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging) return;
            var p = e.GetPosition(this);
            var dx = (float)(p.X - _lastPointer.X);
            var dy = (float)(p.Y - _lastPointer.Y);
            _lastPointer = p;
            Scene.Camera.Orbit(dx * 0.01f, -dy * 0.01f);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _dragging = false;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            var zoom = e.Delta.Y > 0 ? 0.9f : 1.1f;
            Scene.Camera.Orbit(0, 0, zoom);
        }
    }
}
