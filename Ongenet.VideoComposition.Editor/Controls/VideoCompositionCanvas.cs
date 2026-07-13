using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ongenet.Core.Models.Media;
using Ongenet.VideoComposition.Editor.Preview;
using SkiaSharp;

namespace Ongenet.VideoComposition.Editor.Controls;

/// <summary>Preview surface showing the video frame with draggable overlay items and waveform layers.</summary>
public sealed class VideoCompositionCanvas : Control
{
    public static readonly StyledProperty<IVideoPreviewModel?> PreviewModelProperty =
        AvaloniaProperty.Register<VideoCompositionCanvas, IVideoPreviewModel?>(nameof(PreviewModel));

    private VideoLayer? _dragWaveformLayer;
    private VideoLayerItem? _dragItem;
    private Point _dragStart;
    private bool _resizing;
    private (double ox, double oy, double dw, double dh) _letterbox;
    private readonly Dictionary<Guid, SkBitmapPresenter> _engine3DFrames = new();

    public IVideoPreviewModel? PreviewModel
    {
        get => GetValue(PreviewModelProperty);
        set => SetValue(PreviewModelProperty, value);
    }

    static VideoCompositionCanvas()
    {
        PreviewModelProperty.Changed.AddClassHandler<VideoCompositionCanvas>((c, _) => c.InvalidateVisual());
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is IVideoPreviewModel vm)
        {
            PreviewModel = vm;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(IVideoPreviewModel.Frame)
                    or nameof(IVideoPreviewModel.CanvasWidth)
                    or nameof(IVideoPreviewModel.CanvasHeight)
                    or nameof(IVideoPreviewModel.Layers)
                    or nameof(IVideoPreviewModel.PreviewTick)
                    or nameof(IVideoPreviewModel.WaveformRevision)
                    or nameof(IVideoPreviewModel.PlayheadBeats)
                    or nameof(IVideoPreviewModel.ShowSafeAreaOverlay))
                    InvalidateVisual();
            };
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var vm = PreviewModel;
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
        if (vm.ShowSafeAreaOverlay)
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

    private void DrawLayers(DrawingContext ctx, IVideoPreviewModel vm, double ox, double oy, double dw, double dh)
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

            if (layer.IsEngine3DLayer)
            {
                DrawEngine3DLayer(ctx, vm, layer, layerOpacity, ox, oy, dw, dh);
                continue;
            }

            foreach (var item in layer.Items)
            {
                var opacity = layerOpacity * item.Opacity;
                if (opacity <= 0.01) continue;

                var rect = new Rect(ox + item.X * dw, oy + item.Y * dh, item.Width * dw, item.Height * dh);

                if (item.Kind is VideoElementKind.Text or VideoElementKind.Subtitle)
                    DrawTextItem(ctx, item, rect, opacity);
                else if (vm.GetOverlayFrame(layer, item) is { } overlay)
                {
                    using (ctx.PushOpacity(opacity))
                    {
                        if (Math.Abs(item.Rotation) > 1e-6)
                        {
                            var cx = rect.X + rect.Width * 0.5;
                            var cy = rect.Y + rect.Height * 0.5;
                            using (ctx.PushTransform(Matrix.CreateRotation(item.Rotation * Math.PI / 180, new Point(cx, cy))))
                                ctx.DrawImage(overlay, rect);
                        }
                        else
                            ctx.DrawImage(overlay, rect);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(item.SourcePath) && System.IO.File.Exists(item.SourcePath))
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

    private static void DrawTextItem(DrawingContext ctx, VideoLayerItem item, Rect rect, double opacity)
    {
        if (string.IsNullOrWhiteSpace(item.TextContent)) return;
        var color = Color.FromArgb(
            (byte)((item.TextColorArgb >> 24) & 0xFF),
            (byte)((item.TextColorArgb >> 16) & 0xFF),
            (byte)((item.TextColorArgb >> 8) & 0xFF),
            (byte)(item.TextColorArgb & 0xFF));
        var brush = new SolidColorBrush(color);
        using (ctx.PushOpacity(opacity))
        {
            var typeface = new Typeface("Inter");
            var formatted = new FormattedText(item.TextContent, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, item.FontSizePx, brush);
            ctx.DrawText(formatted, rect.TopLeft);
        }
    }

    private void DrawEngine3DBitmap(DrawingContext ctx, VideoLayer layer, Rect rect, double layerOpacity, Func<SKBitmap?> render)
    {
        using var bmp = render();
        if (bmp is null) return;

        if (!_engine3DFrames.TryGetValue(layer.Id, out var presenter))
        {
            presenter = new SkBitmapPresenter();
            _engine3DFrames[layer.Id] = presenter;
        }

        if (presenter.Present(bmp) is { } image)
        {
            using (ctx.PushOpacity(layerOpacity))
                ctx.DrawImage(image, rect);
        }
    }

    private void DrawVisualiserLayer(DrawingContext ctx, IVideoPreviewModel vm, VideoLayer layer,
        double layerOpacity, double ox, double oy, double dw, double dh)
    {
        var rect = new Rect(ox + layer.WaveformX * dw, oy + layer.WaveformY * dh,
            layer.WaveformWidth * dw, layer.WaveformHeight * dh);

        if (layer.WaveformStyle == VideoWaveformStyle.Scope3D && vm.Engine3DRenderer?.IsAvailable == true)
        {
            var rw = Math.Max(16, (int)rect.Width);
            var rh = Math.Max(16, (int)rect.Height);
            DrawEngine3DBitmap(ctx, layer, rect, layerOpacity,
                () => vm.Engine3DRenderer!.RenderWaveformLayer(layer, vm.AudioScope, rw, rh, vm.PreviewDtSeconds));
        }
        else
        {
            var staticWf = !layer.WaveformFollowPlayhead ? vm.GetWaveformForLayer(layer) : null;
            var hasSignal = layer.AudioSourceTrackId is { } id
                && (staticWf is not null
                    || vm.AudioScope.CaptureLatest(id, VideoAudioVisualiserRenderer.SharedSampleBuffer) > 0);

            if (hasSignal)
                VideoAudioVisualiserRenderer.Draw(ctx, layer, vm.AudioScope, rect, layerOpacity, staticWf);
            else
            {
                ctx.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb((byte)(layerOpacity * 80), 137, 180, 250)), null, rect);
            }
        }

        if (vm.IsWaveformLayerSelected(layer))
            DrawBoundsSelection(ctx, rect);
    }

    private static void DrawBoundsSelection(DrawingContext ctx, Rect rect)
    {
        ctx.DrawRectangle(null, new Pen(Brushes.White, 2), rect);
        var handle = new Rect(rect.Right - 12, rect.Bottom - 12, 12, 12);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), null, handle);
        ctx.DrawRectangle(null, new Pen(Brushes.White, 1), handle);
    }

    private void DrawEngine3DLayer(DrawingContext ctx, IVideoPreviewModel vm, VideoLayer layer,
        double layerOpacity, double ox, double oy, double dw, double dh)
    {
        var rect = new Rect(ox + layer.Engine3DX * dw, oy + layer.Engine3DY * dh,
            layer.Engine3DWidth * dw, layer.Engine3DHeight * dh);

        if (vm.Engine3DRenderer?.IsAvailable == true)
        {
            var rw = Math.Max(16, (int)rect.Width);
            var rh = Math.Max(16, (int)rect.Height);
            DrawEngine3DBitmap(ctx, layer, rect, layerOpacity,
                () => vm.Engine3DRenderer!.RenderEngine3DLayer(layer, vm.AudioScope, rw, rh, vm.PreviewDtSeconds));
        }
        else
        {
            ctx.DrawRectangle(
                new SolidColorBrush(Color.FromArgb((byte)(layerOpacity * 80), 166, 227, 161)), null, rect);
        }

        if (vm.IsWaveformLayerSelected(layer))
            DrawBoundsSelection(ctx, rect);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var vm = PreviewModel;
        if (vm is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);
        _dragStart = pos;
        _dragItem = null;
        _dragWaveformLayer = null;
        _resizing = false;

        var hit = HitTestItem(vm, pos, out var layer, out var resize);
        if (hit is not null && layer is not null)
        {
            vm.SelectItem(layer, hit);
            _dragItem = hit;
            _resizing = resize;
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (HitTestWaveformLayer(vm, pos, out var wfLayer, out var wfResize))
        {
            vm.SelectWaveformLayer(wfLayer!);
            _dragWaveformLayer = wfLayer;
            _resizing = wfResize;
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (PreviewModel is not { } vm) return;
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
                if (_dragWaveformLayer.IsEngine3DLayer)
                {
                    vm.SetWaveformBounds(_dragWaveformLayer,
                        _dragWaveformLayer.Engine3DX,
                        _dragWaveformLayer.Engine3DY,
                        Math.Max(0.05, _dragWaveformLayer.Engine3DWidth + dx),
                        Math.Max(0.03, _dragWaveformLayer.Engine3DHeight + dy));
                }
                else
                {
                    vm.SetWaveformBounds(_dragWaveformLayer,
                        _dragWaveformLayer.WaveformX,
                        _dragWaveformLayer.WaveformY,
                        Math.Max(0.05, _dragWaveformLayer.WaveformWidth + dx),
                        Math.Max(0.03, _dragWaveformLayer.WaveformHeight + dy));
                }
            }
            else if (_dragWaveformLayer.IsEngine3DLayer)
            {
                vm.SetWaveformBounds(_dragWaveformLayer,
                    Math.Clamp(_dragWaveformLayer.Engine3DX + dx, 0, 1 - _dragWaveformLayer.Engine3DWidth),
                    Math.Clamp(_dragWaveformLayer.Engine3DY + dy, 0, 1 - _dragWaveformLayer.Engine3DHeight),
                    _dragWaveformLayer.Engine3DWidth,
                    _dragWaveformLayer.Engine3DHeight);
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

    private VideoLayerItem? HitTestItem(IVideoPreviewModel vm, Point p, out VideoLayer? layer, out bool resize)
    {
        layer = null;
        resize = false;
        var (ox, oy, dw, dh) = _letterbox;
        if (dw <= 0 || dh <= 0) return null;

        for (var li = vm.Layers.Count - 1; li >= 0; li--)
        {
            var el = vm.Layers[li];
            if (el.IsWaveformLayer || el.IsEngine3DLayer) continue;
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

    private bool HitTestWaveformLayer(IVideoPreviewModel vm, Point p, out VideoLayer? layer, out bool resize)
    {
        layer = null;
        resize = false;
        var (ox, oy, dw, dh) = _letterbox;
        if (dw <= 0 || dh <= 0) return false;

        for (var li = vm.Layers.Count - 1; li >= 0; li--)
        {
            var el = vm.Layers[li];
            if (el.IsEngine3DLayer)
            {
                var fxRect = new Rect(ox + el.Engine3DX * dw, oy + el.Engine3DY * dh,
                    el.Engine3DWidth * dw, el.Engine3DHeight * dh);
                if (!fxRect.Contains(p)) continue;
                var fxHandle = new Rect(fxRect.Right - 12, fxRect.Bottom - 12, 12, 12);
                resize = fxHandle.Contains(p);
                layer = el;
                return true;
            }

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
