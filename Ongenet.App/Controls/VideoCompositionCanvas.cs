using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ongenet.App.ViewModels.Panels;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Controls;

/// <summary>Preview surface showing the video frame with draggable overlay items and waveform layers.</summary>
public sealed class VideoCompositionCanvas : Control
{
    public static readonly StyledProperty<VideoTrackViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<VideoCompositionCanvas, VideoTrackViewModel?>(nameof(ViewModel));

    private VideoLayer? _dragWaveformLayer;
    private VideoLayerItem? _dragItem;
    private Point _dragStart;
    private bool _resizing;
    private (double ox, double oy, double dw, double dh) _letterbox;

    public VideoTrackViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static VideoCompositionCanvas()
    {
        ViewModelProperty.Changed.AddClassHandler<VideoCompositionCanvas>((c, _) => c.InvalidateVisual());
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is VideoTrackViewModel vm)
        {
            ViewModel = vm;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(VideoTrackViewModel.Frame)
                    or nameof(VideoTrackViewModel.CanvasWidth)
                    or nameof(VideoTrackViewModel.CanvasHeight)
                    or nameof(VideoTrackViewModel.Layers)
                    or nameof(VideoTrackViewModel.SelectedLayerItem)
                    or nameof(VideoTrackViewModel.PreviewTick)
                    or nameof(VideoTrackViewModel.WaveformRevision)
                    or nameof(VideoTrackViewModel.PlayheadBeats))
                    InvalidateVisual();
            };
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var vm = ViewModel;
        if (vm is null) return;

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        _letterbox = ComputeLetterbox(w, h, vm.CanvasWidth, vm.CanvasHeight);
        var (ox, oy, dw, dh) = _letterbox;

        if (vm.Frame is { } frame)
            context.DrawImage(frame, new Rect(ox, oy, dw, dh));
        else
            DrawCheckerboard(context, ox, oy, dw, dh);

        DrawLayers(context, vm, ox, oy, dw, dh);
        DrawSafeMargin(context, ox, oy, dw, dh);
    }

    private static (double ox, double oy, double dw, double dh) ComputeLetterbox(
        double viewW, double viewH, int canvasW, int canvasH)
    {
        var aspect = canvasW / (double)Math.Max(1, canvasH);
        double dw, dh;
        if (viewW / viewH > aspect)
        {
            dh = viewH;
            dw = dh * aspect;
        }
        else
        {
            dw = viewW;
            dh = dw / aspect;
        }

        return ((viewW - dw) * 0.5, (viewH - dh) * 0.5, dw, dh);
    }

    private static void DrawCheckerboard(DrawingContext ctx, double ox, double oy, double dw, double dh)
    {
        var light = Color.FromRgb(42, 42, 58);
        var dark = Color.FromRgb(30, 30, 46);
        ctx.DrawRectangle(new SolidColorBrush(dark), null, new Rect(ox, oy, dw, dh));
        var cell = Math.Max(8, Math.Min(dw, dh) / 16);
        var cols = (int)Math.Ceiling(dw / cell);
        var rows = (int)Math.Ceiling(dh / cell);
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                if ((row + col) % 2 == 0) continue;
                ctx.DrawRectangle(new SolidColorBrush(light), null,
                    new Rect(ox + col * cell, oy + row * cell, cell, cell));
            }
        }
    }

    private static void DrawSafeMargin(DrawingContext ctx, double ox, double oy, double dw, double dh)
    {
        var insetX = dw * 0.05;
        var insetY = dh * 0.05;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
        ctx.DrawRectangle(null, pen, new Rect(ox + insetX, oy + insetY, dw - insetX * 2, dh - insetY * 2));
    }

    private static void DrawLayers(DrawingContext ctx, VideoTrackViewModel vm, double ox, double oy, double dw, double dh)
    {
        foreach (var layer in vm.Layers)
        {
            var layerOpacity = vm.GetLayerOpacity(layer.Id);
            if (layerOpacity <= 0.01) continue;

            if (layer.IsWaveformLayer)
            {
                DrawVisualiserLayer(ctx, vm, layer, layerOpacity, ox, oy, dw, dh);
                continue;
            }

            foreach (var item in layer.Items)
            {
                var opacity = layerOpacity * item.Opacity;
                if (opacity <= 0.01) continue;

                var rect = new Rect(ox + item.X * dw, oy + item.Y * dh, item.Width * dw, item.Height * dh);
                if (!string.IsNullOrWhiteSpace(item.SourcePath) && System.IO.File.Exists(item.SourcePath))
                {
                    try
                    {
                        using var bmp = new Bitmap(item.SourcePath);
                        using (ctx.PushOpacity(opacity))
                            ctx.DrawImage(bmp, rect);
                    }
                    catch
                    {
                        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(opacity * 120), 166, 227, 161)), null, rect);
                    }
                }
                else
                {
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(opacity * 100), 250, 179, 135)), null, rect);
                }

                if (vm.IsItemSelected(layer, item))
                    ctx.DrawRectangle(null, new Pen(Brushes.White, 2), rect);
            }
        }
    }

    private static void DrawVisualiserLayer(DrawingContext ctx, VideoTrackViewModel vm, VideoLayer layer,
        double layerOpacity, double ox, double oy, double dw, double dh)
    {
        var rect = new Rect(ox + layer.WaveformX * dw, oy + layer.WaveformY * dh,
            layer.WaveformWidth * dw, layer.WaveformHeight * dh);

        var hasSignal = layer.AudioSourceTrackId is { } id
            && vm.AudioScope.CaptureLatest(id, VideoAudioVisualiserRenderer.SharedSampleBuffer) > 0;

        if (hasSignal)
            VideoAudioVisualiserRenderer.Draw(ctx, layer, vm.AudioScope, rect, layerOpacity);
        else
        {
            ctx.DrawRectangle(
                new SolidColorBrush(Color.FromArgb((byte)(layerOpacity * 80), 137, 180, 250)), null, rect);
        }

        if (vm.IsWaveformLayerSelected(layer))
            ctx.DrawRectangle(null, new Pen(Brushes.White, 2), rect);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var vm = ViewModel;
        if (vm is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var hit = HitTestItem(vm, e.GetPosition(this), out var layer, out var resize);
        if (hit is not null && layer is not null)
            vm.SelectItem(layer, hit);
        else if (HitTestWaveformLayer(vm, e.GetPosition(this), out var wfLayer, out var wfResize))
        {
            vm.SelectWaveformLayer(wfLayer!);
            _dragWaveformLayer = wfLayer;
            _resizing = wfResize;
        }

        _dragItem = hit;
        _dragStart = e.GetPosition(this);
        _resizing = resize;
        e.Handled = hit is not null || _dragWaveformLayer is not null;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ViewModel is not { } vm) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);
        var (_, _, dw, dh) = _letterbox;
        if (dw <= 0 || dh <= 0) return;

        var dx = (pos.X - _dragStart.X) / dw;
        var dy = (pos.Y - _dragStart.Y) / dh;
        _dragStart = pos;

        if (_dragWaveformLayer is not null)
        {
            if (_resizing)
            {
                vm.SetWaveformBounds(_dragWaveformLayer,
                    _dragWaveformLayer.WaveformX,
                    _dragWaveformLayer.WaveformY,
                    Math.Max(0.05, _dragWaveformLayer.WaveformWidth + dx),
                    Math.Max(0.03, _dragWaveformLayer.WaveformHeight + dy));
            }
            else
            {
                vm.SetWaveformBounds(_dragWaveformLayer,
                    Math.Clamp(_dragWaveformLayer.WaveformX + dx, 0, 1 - _dragWaveformLayer.WaveformWidth),
                    Math.Clamp(_dragWaveformLayer.WaveformY + dy, 0, 1 - _dragWaveformLayer.WaveformHeight),
                    _dragWaveformLayer.WaveformWidth,
                    _dragWaveformLayer.WaveformHeight);
            }

            InvalidateVisual();
            return;
        }

        if (_dragItem is null) return;

        if (_resizing)
            vm.ResizeElement(_dragItem.Id, Math.Max(0.02, _dragItem.Width + dx), Math.Max(0.02, _dragItem.Height + dy));
        else
            vm.MoveElement(_dragItem.Id, _dragItem.X + dx, _dragItem.Y + dy);

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragItem = null;
        _dragWaveformLayer = null;
        _resizing = false;
    }

    private VideoLayerItem? HitTestItem(VideoTrackViewModel vm, Point p, out VideoLayer? layer, out bool resize)
    {
        layer = null;
        resize = false;
        var (ox, oy, dw, dh) = _letterbox;
        if (dw <= 0 || dh <= 0) return null;

        for (var li = vm.Layers.Count - 1; li >= 0; li--)
        {
            var el = vm.Layers[li];
            if (el.IsWaveformLayer) continue;
            for (var ii = el.Items.Count - 1; ii >= 0; ii--)
            {
                var item = el.Items[ii];
                var rect = new Rect(ox + item.X * dw, oy + item.Y * dh, item.Width * dw, item.Height * dh);
                if (!rect.Contains(p)) continue;
                var handle = new Rect(rect.Right - 12, rect.Bottom - 12, 12, 12);
                resize = handle.Contains(p);
                layer = el;
                return item;
            }
        }

        return null;
    }

    private bool HitTestWaveformLayer(VideoTrackViewModel vm, Point p, out VideoLayer? layer, out bool resize)
    {
        layer = null;
        resize = false;
        var (ox, oy, dw, dh) = _letterbox;
        if (dw <= 0 || dh <= 0) return false;

        for (var li = vm.Layers.Count - 1; li >= 0; li--)
        {
            var el = vm.Layers[li];
            if (!el.IsWaveformLayer) continue;
            var rect = new Rect(ox + el.WaveformX * dw, oy + el.WaveformY * dh,
                el.WaveformWidth * dw, el.WaveformHeight * dh);
            if (!rect.Contains(p)) continue;
            var handle = new Rect(rect.Right - 12, rect.Bottom - 12, 12, 12);
            resize = handle.Contains(p);
            layer = el;
            return true;
        }

        return false;
    }
}
