using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using SkiaSharp;

namespace Ongenet.Core.Video;

/// <summary>Assets loaded once for a composited export pass.</summary>
public sealed class VideoCompositionExportAssets : IDisposable
{
    private readonly Dictionary<string, SKBitmap> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, SequentialVideoReader> _videos = new();

    public IReadOnlyDictionary<Guid, AudioSampleBuffer> StemBuffers { get; init; } =
        new Dictionary<Guid, AudioSampleBuffer>();

    public SKBitmap? GetImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (_images.TryGetValue(path, out var cached)) return cached;
        try
        {
            var bmp = SKBitmap.Decode(path);
            if (bmp is null) return null;
            _images[path] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public SKBitmap? ReadVideoFrame(Guid consumerId, string path, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (!_videos.TryGetValue(consumerId, out var reader))
        {
            reader = new SequentialVideoReader();
            _videos[consumerId] = reader;
        }

        return reader.ReadNext(path, width, height);
    }

    public void Dispose()
    {
        foreach (var bmp in _images.Values) bmp.Dispose();
        _images.Clear();
        foreach (var reader in _videos.Values) reader.Dispose();
        _videos.Clear();
    }
}

internal sealed class SequentialVideoReader : IDisposable
{
    private readonly LiveVideoDecoder _decoder = new();
    private string _path = string.Empty;
    private int _width;
    private int _height;

    public SKBitmap? ReadNext(string path, int width, int height)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        if (_path != path || _width != width || _height != height || !_decoder.IsRunning)
        {
            _path = path;
            _width = width;
            _height = height;
            if (!_decoder.Open(path, 0, width, height)) return null;
        }

        var rgb = _decoder.ReadFrame();
        if (rgb is null) return null;
        return RgbToBitmap(rgb, width, height);
    }

    private static SKBitmap RgbToBitmap(byte[] rgb, int width, int height)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var pixels = bmp.GetPixels();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                var color = new SKColor(rgb[i], rgb[i + 1], rgb[i + 2]);
                bmp.SetPixel(x, y, color);
            }
        }

        return bmp;
    }

    public void Dispose() => _decoder.Dispose();
}

/// <summary>Renders one composited frame matching the in-app video composition canvas.</summary>
public static class VideoCompositionFrameRenderer
{
    private static readonly SKColor Background = new(0x1e, 0x1e, 0x2e);

    public static void Render(SKCanvas canvas, Project project, double timeSeconds,
        VideoCompositionRuntime runtime, OfflineVideoAudioScope scope, VideoCompositionExportAssets assets,
        int width, int height)
    {
        canvas.Clear(Background);
        scope.SetTime(timeSeconds);

        foreach (var layer in project.VideoLayers.OrderBy(l => l.ZOrder))
        {
            var layerOpacity = runtime.GetOpacity(layer.Id);
            if (layerOpacity <= 0.01) continue;

            if (layer.IsWaveformLayer)
            {
                var rect = new SKRect(
                    (float)(layer.WaveformX * width),
                    (float)(layer.WaveformY * height),
                    (float)((layer.WaveformX + layer.WaveformWidth) * width),
                    (float)((layer.WaveformY + layer.WaveformHeight) * height));
                VideoAudioVisualiserSkiaRenderer.Draw(canvas, layer, scope, rect, layerOpacity);
                continue;
            }

            foreach (var item in layer.Items)
            {
                if (item.Kind is VideoElementKind.Waveform) continue;
                var opacity = (float)(layerOpacity * item.Opacity);
                if (opacity <= 0.01f) continue;

                var dest = new SKRect(
                    (float)(item.X * width),
                    (float)(item.Y * height),
                    (float)((item.X + item.Width) * width),
                    (float)((item.Y + item.Height) * height));

                SKBitmap? frame = item.Kind switch
                {
                    VideoElementKind.Video => assets.ReadVideoFrame(item.Id, item.SourcePath, (int)dest.Width, (int)dest.Height),
                    _ => assets.GetImage(item.SourcePath)
                };

                if (frame is null) continue;
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Color = SKColors.White.WithAlpha((byte)(opacity * 255))
                };
                canvas.DrawBitmap(frame, dest, paint);
                if (item.Kind == VideoElementKind.Video)
                    frame.Dispose();
            }
        }
    }
}
