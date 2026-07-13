using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using Ongenet.VideoComposition.Ffmpeg;
using SkiaSharp;

namespace Ongenet.VideoComposition.Rendering;

/// <summary>Assets loaded once for a composited export pass.</summary>
public sealed class VideoCompositionExportAssets : IDisposable
{
    private readonly Dictionary<string, SKBitmap> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SeekingVideoReader> _videos = new(StringComparer.OrdinalIgnoreCase);
    private readonly IVideoFrameExtractor _frameExtractor;

    public VideoCompositionExportAssets(IVideoFrameExtractor frameExtractor) =>
        _frameExtractor = frameExtractor;

    public IReadOnlyDictionary<Guid, AudioSampleBuffer> StemBuffers { get; init; } =
        new Dictionary<Guid, AudioSampleBuffer>();

    public IReadOnlyDictionary<Guid, AudioWaveform>? Waveforms { get; init; }

    public IVideoEngine3DLayerRenderer? Engine3DRenderer { get; init; }

    public double Engine3DFrameDt { get; init; } = 1.0 / 30.0;

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

    public SKBitmap? ReadVideoFrame(string path, double timeSeconds, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (!_videos.TryGetValue(path, out var reader))
        {
            reader = new SeekingVideoReader();
            _videos[path] = reader;
        }

        return reader.ReadAt(path, timeSeconds, width, height);
    }

    public SKBitmap? ReadAnimatedFrame(string path, double timeSeconds, int width, int height)
    {
        var png = _frameExtractor.ExtractFramePng(path, Math.Max(0, timeSeconds));
        if (png is null) return null;
        using var ms = new MemoryStream(png);
        return SKBitmap.Decode(ms);
    }

    public void Dispose()
    {
        foreach (var bmp in _images.Values) bmp.Dispose();
        _images.Clear();
        foreach (var reader in _videos.Values) reader.Dispose();
        _videos.Clear();
    }
}

internal sealed class SeekingVideoReader : IDisposable
{
    private readonly LiveVideoDecoder _decoder = new();
    private string _path = string.Empty;
    private int _width;
    private int _height;
    private double _lastTime = double.NaN;

    public SKBitmap? ReadAt(string path, double timeSeconds, int width, int height)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        if (_path != path || _width != width || _height != height
            || Math.Abs(timeSeconds - _lastTime) > 0.04 || !_decoder.IsRunning)
        {
            _path = path;
            _width = width;
            _height = height;
            _lastTime = timeSeconds;
            if (!_decoder.Open(path, Math.Max(0, timeSeconds), width, height)) return null;
        }

        var rgb = _decoder.ReadFrame();
        if (rgb is null) return null;
        return VideoFrameBitmap.RgbToBitmap(rgb, width, height);
    }

    public void Dispose() => _decoder.Dispose();
}

internal static class VideoFrameBitmap
{
    public static SKBitmap RgbToBitmap(byte[] rgb, int width, int height)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var pixels = bmp.GetPixels();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                bmp.SetPixel(x, y, new SKColor(rgb[i], rgb[i + 1], rgb[i + 2]));
            }
        }

        return bmp;
    }
}

/// <summary>Renders one composited frame matching the in-app video composition canvas.</summary>
public static class VideoCompositionFrameRenderer
{
    private static readonly SKColor Background = new(0x1e, 0x1e, 0x2e);

    public static void Render(SKCanvas canvas, Project project, double timeSeconds, double playheadBeats,
        VideoCompositionRuntime runtime, OfflineVideoAudioScope scope, VideoCompositionExportAssets assets,
        int width, int height, Func<Project, double, double>? beatsToSeconds = null)
    {
        canvas.Clear(Background);
        scope.SetTime(beatsToSeconds is not null
            ? beatsToSeconds(project, playheadBeats)
            : timeSeconds);

        foreach (var layer in project.VideoLayers.OrderBy(l => l.ZOrder))
        {
            var layerOpacity = runtime.GetOpacity(layer.Id);
            if (layerOpacity <= 0.01) continue;

            var layerTime = VideoCompositionTimeMapper.ComputeLayerTimeSeconds(
                layer, timeSeconds, project, beatsToSeconds, playheadBeats);
            if (!VideoCompositionTimeMapper.IsLayerActiveAtTime(layer, layerTime))
                continue;

            if (layer.IsWaveformLayer)
            {
                var rect = new SKRect(
                    (float)(layer.WaveformX * width),
                    (float)(layer.WaveformY * height),
                    (float)((layer.WaveformX + layer.WaveformWidth) * width),
                    (float)((layer.WaveformY + layer.WaveformHeight) * height));

                if (layer.WaveformStyle == VideoWaveformStyle.Scope3D && assets.Engine3DRenderer?.IsAvailable == true)
                {
                    var rw = Math.Max(16, (int)rect.Width);
                    var rh = Math.Max(16, (int)rect.Height);
                    if (assets.Engine3DRenderer.RenderWaveformLayer(layer, scope, rw, rh, assets.Engine3DFrameDt)
                        is { } scopeBmp)
                    {
                        DrawPremulBitmapItem(canvas, scopeBmp, rect, 0, (float)layerOpacity, layer.BlendMode);
                        scopeBmp.Dispose();
                    }

                    continue;
                }

                AudioWaveform? wf = null;
                if (!layer.WaveformFollowPlayhead && layer.AudioSourceTrackId is { } tid
                    && assets.Waveforms?.TryGetValue(tid, out var cached) == true)
                    wf = cached;
                VideoAudioVisualiserSkiaRenderer.Draw(canvas, layer, scope, rect, layerOpacity, wf, layerTime);
                continue;
            }

            if (layer.IsEngine3DLayer && assets.Engine3DRenderer?.IsAvailable == true)
            {
                var rect = new SKRect(
                    (float)(layer.Engine3DX * width),
                    (float)(layer.Engine3DY * height),
                    (float)((layer.Engine3DX + layer.Engine3DWidth) * width),
                    (float)((layer.Engine3DY + layer.Engine3DHeight) * height));
                var rw = Math.Max(16, (int)rect.Width);
                var rh = Math.Max(16, (int)rect.Height);
                if (assets.Engine3DRenderer.RenderEngine3DLayer(layer, scope, rw, rh, assets.Engine3DFrameDt)
                    is { } fxBmp)
                {
                    DrawPremulBitmapItem(canvas, fxBmp, rect, 0, (float)layerOpacity, layer.BlendMode);
                    fxBmp.Dispose();
                }

                continue;
            }

            foreach (var item in layer.Items)
            {
                if (item.Kind is VideoElementKind.Waveform) continue;
                var (kx, ky, kw, kh, kOpacity) = VideoKeyframeInterpolator.Resolve(item, project, playheadBeats);
                var opacity = (float)(layerOpacity * kOpacity);
                if (opacity <= 0.01f) continue;

                var dest = new SKRect(
                    (float)(kx * width),
                    (float)(ky * height),
                    (float)((kx + kw) * width),
                    (float)((ky + kh) * height));

                if (item.Kind == VideoElementKind.Text)
                {
                    DrawTextItem(canvas, item, dest, opacity);
                    continue;
                }

                if (item.Kind == VideoElementKind.Subtitle)
                {
                    var text = VideoSubtitleResolver.ResolveText(item, project, layerTime, beatsToSeconds, playheadBeats);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var subItem = new VideoLayerItem
                        {
                            TextContent = text,
                            FontSizePx = item.FontSizePx,
                            TextColorArgb = item.TextColorArgb
                        };
                        DrawTextItem(canvas, subItem, dest, opacity);
                    }

                    continue;
                }

                if (item.Kind == VideoElementKind.Engine3D)
                {
                    if (!string.IsNullOrWhiteSpace(item.SourcePath) && assets.GetImage(item.SourcePath) is { } snap)
                    {
                        using var processedSnap = VideoCompositionEffects.ApplyItemEffects(snap, item, assets);
                        DrawBitmapItem(canvas, processedSnap, dest, item.Rotation, opacity, layer.BlendMode);
                    }

                    continue;
                }

                SKBitmap? frame = item.Kind switch
                {
                    VideoElementKind.Video => assets.ReadVideoFrame(item.SourcePath, layerTime,
                        Math.Max(16, (int)dest.Width), Math.Max(16, (int)dest.Height)),
                    VideoElementKind.AnimatedGif => assets.ReadAnimatedFrame(item.SourcePath, layerTime,
                        Math.Max(16, (int)dest.Width), Math.Max(16, (int)dest.Height)),
                    _ => assets.GetImage(item.SourcePath)
                };

                if (frame is null) continue;
                var needsDispose = item.Kind is VideoElementKind.Video or VideoElementKind.AnimatedGif;
                var processed = VideoCompositionEffects.ApplyItemEffects(frame, item, assets);
                DrawBitmapItem(canvas, processed, dest, item.Rotation, opacity, layer.BlendMode);
                if (!ReferenceEquals(processed, frame)) processed.Dispose();
                if (needsDispose) frame.Dispose();
            }
        }
    }

    private static void DrawBitmapItem(SKCanvas canvas, SKBitmap frame, SKRect dest, double rotation, float opacity,
        VideoBlendMode blendMode = VideoBlendMode.Normal)
    {
        DrawPremulBitmapItem(canvas, frame, dest, rotation, opacity, blendMode, premultiplied: false);
    }

    private static void DrawPremulBitmapItem(SKCanvas canvas, SKBitmap frame, SKRect dest, double rotation, float opacity,
        VideoBlendMode blendMode = VideoBlendMode.Normal, bool premultiplied = true)
    {
        canvas.Save();
        if (Math.Abs(rotation) > 1e-6)
        {
            var cx = dest.MidX;
            var cy = dest.MidY;
            canvas.Translate(cx, cy);
            canvas.RotateDegrees((float)rotation);
            canvas.Translate(-cx, -cy);
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = premultiplied
                ? SKColors.White.WithAlpha((byte)(opacity * 255))
                : SKColors.White.WithAlpha((byte)(opacity * 255)),
            BlendMode = VideoCompositionEffects.ToSkBlendMode(blendMode)
        };
        canvas.DrawBitmap(frame, dest, paint);
        canvas.Restore();
    }

    private static void DrawTextItem(SKCanvas canvas, VideoLayerItem item, SKRect dest, float opacity)
    {
        if (string.IsNullOrWhiteSpace(item.TextContent)) return;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = (float)Math.Clamp(item.FontSizePx, 8, 256),
            Color = ToSkColor(item.TextColorArgb).WithAlpha((byte)(opacity * 255)),
            Typeface = SKTypeface.FromFamilyName("Inter", SKFontStyle.Normal)
        };
        canvas.DrawText(item.TextContent, dest.Left, dest.Top + paint.TextSize, paint);
    }

    private static SKColor ToSkColor(uint argb) =>
        new((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));
}
