using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.App.Controls;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Views.Panels
{
    public partial class SampleInspectorView : UserControl
    {
        private const double ZoomSensitivity = 0.005;

        private bool _zooming;
        private double _zoomStartScale;
        private double _zoomAnchorSeconds;
        private double _zoomStartY;

        public SampleInspectorView()
        {
            InitializeComponent();
            WaveEditor.TrimCommitted += OnTrimCommitted;
            WaveEditor.SelectionChanged += OnSelectionChanged;
            WaveEditor.MoveStarted += OnMoveStarted;
            WaveEditor.MoveCommitted += OnMoveCommitted;
            WaveEditor.HoverChanged += OnHoverChanged;

            WaveScroll.SizeChanged += OnWaveScrollSizeChanged;
            WaveScroll.ScrollChanged += OnWaveScrollChanged;
            WaveScroll.AddHandler(PointerPressedEvent, OnWaveLeftPointerPressed, RoutingStrategies.Tunnel);
            WaveScroll.AddHandler(PointerPressedEvent, OnWavePointerPressed, RoutingStrategies.Tunnel);
            WaveScroll.AddHandler(PointerMovedEvent, OnWavePointerMoved, RoutingStrategies.Tunnel);
            WaveScroll.AddHandler(PointerReleasedEvent, OnWavePointerReleased, RoutingStrategies.Tunnel);
            WaveScroll.AddHandler(PointerWheelChangedEvent, OnWavePointerWheel, RoutingStrategies.Tunnel);

            GainKnob.PointerPressed += OnGainKnobPressed;
            GainKnob.DragCompleted += OnGainKnobDragCompleted;
            PanKnob.PointerPressed += OnPanKnobPressed;
            PanKnob.DragCompleted += OnPanKnobDragCompleted;

            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }

        private SampleInspectorViewModel? Vm => DataContext as SampleInspectorViewModel;

        private void OnTrimCommitted(object? sender, EventArgs e) => Vm?.OnTrimCommitted();

        private void OnSelectionChanged(object? sender, EventArgs e) => Vm?.OnEditorSelectionChanged();

        private void OnMoveStarted(object? sender, EventArgs e) => Vm?.OnMoveStarted();

        private void OnMoveCommitted(object? sender, EventArgs e) => Vm?.OnMoveCommitted();

        private void OnHoverChanged(object? sender, EventArgs e)
        {
            if (Vm is null) return;
            Vm.HoverSeconds = WaveEditor.HoverSeconds;
        }

        private void OnWaveScrollSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            SyncWaveScrollOffset();
            if (Vm is null || WaveScroll.Viewport.Width <= 0) return;
            Vm.ViewportWidth = WaveScroll.Viewport.Width;
        }

        private void SyncWaveScrollOffset() => WaveEditor.HorizontalOffset = WaveScroll.Offset.X;

        private void OnWaveScrollChanged(object? sender, ScrollChangedEventArgs e) => SyncWaveScrollOffset();

        private void OnWaveLeftPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (Vm is null || !Vm.HasSample || Vm.DurationSeconds <= 0) return;
            var pt = e.GetCurrentPoint(WaveScroll);
            if (!pt.Properties.IsLeftButtonPressed || e.ClickCount < 2) return;

            var pos = pt.Position;
            var contentX = pos.X + WaveScroll.Offset.X;
            var width = Math.Max(1, Vm.ContentWidth);
            var seconds = Math.Clamp(contentX / width * Vm.DurationSeconds, 0, Vm.DurationSeconds);
            Vm.PlayFromSeconds(seconds);
            e.Handled = true;
        }

        private void OnWavePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (Vm is null || !Vm.HasSample) return;
            var point = e.GetCurrentPoint(WaveScroll);
            if (!point.Properties.IsMiddleButtonPressed) return;

            var pos = point.Position;
            var editorX = pos.X + WaveScroll.Offset.X;
            var contentWidth = Math.Max(1, Vm.ContentWidth);
            _zoomAnchorSeconds = editorX / contentWidth * Vm.DurationSeconds;
            _zoomStartScale = Vm.ZoomScale;
            _zoomStartY = pos.Y;
            _zooming = true;
            e.Pointer.Capture(WaveScroll);
            e.Handled = true;
        }

        private void OnWavePointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_zooming || Vm is null) return;

            var pos = e.GetPosition(WaveScroll);
            var newScale = _zoomStartScale * Math.Exp(-(pos.Y - _zoomStartY) * ZoomSensitivity);
            Vm.ZoomScale = newScale;

            var contentWidth = Math.Max(1, Vm.ContentWidth);
            var anchorX = Vm.DurationSeconds > 0 ? _zoomAnchorSeconds / Vm.DurationSeconds * contentWidth : 0;
            var scrollX = Math.Max(0, anchorX - pos.X);
            WaveScroll.Offset = new Vector(scrollX, WaveScroll.Offset.Y);
            SyncWaveScrollOffset();

            e.Handled = true;
        }

        private void OnWavePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_zooming) return;
            _zooming = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void OnWavePointerWheel(object? sender, PointerWheelEventArgs e)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
            if (Vm is null || !Vm.HasSample) return;

            var pos = e.GetPosition(WaveScroll);
            var scrollX = WaveScroll.Offset.X;
            var contentWidth = ShiftScrollZoom.SecondsContentWidth(Vm.ViewportWidth, Vm.ZoomScale);
            var anchorSeconds = Vm.DurationSeconds > 0
                ? (pos.X + scrollX) / contentWidth * Vm.DurationSeconds
                : 0;
            var (zoomDelta, panDelta) = ShiftScrollZoom.ResolveWheelDeltas(e.Delta);
            if (Math.Abs(zoomDelta) < 1e-6 && Math.Abs(panDelta) < 1e-6) return;

            ShiftScrollZoom.ApplySecondsTimeline(
                anchorSeconds, pos, Vm.DurationSeconds, Vm.ViewportWidth,
                Vm.ZoomScale, zoomDelta, panDelta, scrollX,
                out var newZoomScale, out var newScrollX);

            Vm.ZoomScale = newZoomScale;
            WaveScroll.Offset = new Vector(newScrollX, WaveScroll.Offset.Y);
            SyncWaveScrollOffset();
            e.Handled = true;
        }

        private void OnGainKnobPressed(object? sender, PointerPressedEventArgs e)
        {
            if (Vm is null || !Vm.HasSelection) return;
            Vm.BeginSelectionEdit("Adjust selection gain");
        }

        private void OnGainKnobDragCompleted(object? sender, EventArgs e)
        {
            if (Vm is null) return;
            Vm.SelectionGainDb = GainKnob.Value;
            if (Vm.ApplySelectionGain())
                Vm.OnEditorSelectionChanged();
        }

        private void OnPanKnobPressed(object? sender, PointerPressedEventArgs e)
        {
            if (Vm is null || !Vm.HasSelection || !Vm.CanEditSelectionPan) return;
            Vm.BeginSelectionEdit("Adjust selection pan");
        }

        private void OnPanKnobDragCompleted(object? sender, EventArgs e)
        {
            if (Vm is null) return;
            Vm.SelectionPan = PanKnob.Value;
            if (Vm.ApplySelectionPan())
                Vm.OnEditorSelectionChanged();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (Vm is null || !Vm.HasSample) return;

            if (e.Key == Key.Delete)
            {
                Vm.HandleDeleteKey();
                e.Handled = true;
                return;
            }

            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

            switch (e.Key)
            {
                case Key.X: Vm.HandleCutKey(); e.Handled = true; break;
                case Key.C: Vm.HandleCopyKey(); e.Handled = true; break;
                case Key.V: Vm.HandlePasteKey(); e.Handled = true; break;
            }
        }
    }
}
