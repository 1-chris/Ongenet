using System;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Core.Video;
using SkiaSharp;

namespace Ongenet.App.Controls;

/// <summary>Draws live audio visualisers on the video composition canvas.</summary>
public static class VideoAudioVisualiserRenderer
{
    public static readonly float[] SharedSampleBuffer = VideoAudioVisualiserSkiaRenderer.SharedSampleBuffer;

    public static void Draw(DrawingContext ctx, VideoLayer layer, IVideoAudioScopeService scope,
        Rect rect, double layerOpacity)
    {
        var w = Math.Max(1, (int)rect.Width);
        var h = Math.Max(1, (int)rect.Height);
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        VideoAudioVisualiserSkiaRenderer.Draw(canvas, layer, scope, new SKRect(0, 0, w, h), layerOpacity);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null) return;
        using var ms = new MemoryStream(data.ToArray());
        using var bmp = new Bitmap(ms);
        ctx.DrawImage(bmp, rect);
    }
}
