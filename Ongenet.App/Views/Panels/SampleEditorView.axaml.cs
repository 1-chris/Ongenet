using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ongenet.App.Controls;
using Ongenet.App.ViewModels;

namespace Ongenet.App.Views.Panels;

public partial class SampleEditorView : UserControl
{
    private const double ZoomSensitivity = 0.005;

    private bool _zooming;
    private double _zoomStartScale;
    private double _zoomAnchorSeconds;
    private double _zoomStartY;

    public SampleEditorView()
    {
        InitializeComponent();
        WaveEditor.TrimCommitted += (_, _) => Vm?.OnTrimCommitted();
        WaveEditor.SelectionChanged += (_, _) => Vm?.OnEditorSelectionChanged();
        WaveEditor.MoveStarted += (_, _) => Vm?.OnMoveStarted();
        WaveEditor.MoveCommitted += (_, _) => Vm?.OnMoveCommitted();
        WaveEditor.HoverChanged += (_, _) =>
        {
            if (Vm is not null) Vm.HoverSeconds = WaveEditor.HoverSeconds;
        };

        WaveScroll.SizeChanged += OnWaveScrollSizeChanged;
        WaveScroll.ScrollChanged += (_, _) => WaveEditor.HorizontalOffset = WaveScroll.Offset.X;
        WaveScroll.AddHandler(PointerPressedEvent, OnWaveLeftPointerPressed, RoutingStrategies.Tunnel);
        WaveScroll.AddHandler(PointerPressedEvent, OnWavePointerPressed, RoutingStrategies.Tunnel);
        WaveScroll.AddHandler(PointerMovedEvent, OnWavePointerMoved, RoutingStrategies.Tunnel);
        WaveScroll.AddHandler(PointerReleasedEvent, OnWavePointerReleased, RoutingStrategies.Tunnel);
        WaveScroll.AddHandler(PointerWheelChangedEvent, OnWavePointerWheel, RoutingStrategies.Tunnel);

        GainKnob.PointerPressed += (_, e) =>
        {
            if (Vm is not null && Vm.HasSelection) Vm.BeginSelectionEdit("Adjust selection gain");
        };
        GainKnob.DragCompleted += (_, _) =>
        {
            if (Vm is null) return;
            Vm.SelectionGainDb = GainKnob.Value;
            if (Vm.ApplySelectionGain()) Vm.OnEditorSelectionChanged();
        };
        PanKnob.PointerPressed += (_, _) =>
        {
            if (Vm is not null && Vm.HasSelection && Vm.CanEditSelectionPan)
                Vm.BeginSelectionEdit("Adjust selection pan");
        };
        PanKnob.DragCompleted += (_, _) =>
        {
            if (Vm is null) return;
            Vm.SelectionPan = PanKnob.Value;
            if (Vm.ApplySelectionPan()) Vm.OnEditorSelectionChanged();
        };

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private SampleEditorCoreViewModel? Vm => DataContext as SampleEditorCoreViewModel;

    private void OnWaveScrollSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        WaveEditor.HorizontalOffset = WaveScroll.Offset.X;
        if (Vm is null || WaveScroll.Viewport.Width <= 0) return;
        Vm.ViewportWidth = WaveScroll.Viewport.Width;
    }

    private void OnWaveLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null || !Vm.HasSample || Vm.DurationSeconds <= 0) return;
        var pt = e.GetCurrentPoint(WaveScroll);
        if (!pt.Properties.IsLeftButtonPressed || e.ClickCount < 2) return;
        var contentX = pt.Position.X + WaveScroll.Offset.X;
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
        var editorX = point.Position.X + WaveScroll.Offset.X;
        var contentWidth = Math.Max(1, Vm.ContentWidth);
        _zoomAnchorSeconds = editorX / contentWidth * Vm.DurationSeconds;
        _zoomStartScale = Vm.ZoomScale;
        _zoomStartY = point.Position.Y;
        _zooming = true;
        e.Pointer.Capture(WaveScroll);
        e.Handled = true;
    }

    private void OnWavePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_zooming || Vm is null) return;
        var pos = e.GetPosition(WaveScroll);
        Vm.ZoomScale = _zoomStartScale * Math.Exp(-(pos.Y - _zoomStartY) * ZoomSensitivity);
        var contentWidth = Math.Max(1, Vm.ContentWidth);
        var anchorX = Vm.DurationSeconds > 0 ? _zoomAnchorSeconds / Vm.DurationSeconds * contentWidth : 0;
        WaveScroll.Offset = new Vector(Math.Max(0, anchorX - pos.X), WaveScroll.Offset.Y);
        WaveEditor.HorizontalOffset = WaveScroll.Offset.X;
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
        WaveEditor.HorizontalOffset = WaveScroll.Offset.X;
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is null || !Vm.HasSample) return;
        if (e.Key == Key.Delete) { Vm.HandleDeleteKey(); e.Handled = true; return; }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        switch (e.Key)
        {
            case Key.X: Vm.HandleCutKey(); e.Handled = true; break;
            case Key.C: Vm.HandleCopyKey(); e.Handled = true; break;
            case Key.V: Vm.HandlePasteKey(); e.Handled = true; break;
        }
    }
}
