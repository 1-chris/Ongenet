using System;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;
using SkiaSharp;

namespace Ongenet.VideoComposition.Rendering;

/// <summary>Draws audio visualisers with Skia — shared by live preview and offline export.</summary>
public static class VideoAudioVisualiserSkiaRenderer
{
    private const int CaptureSize = 2048;
    private const int FftSize = 1024;
    private const int BarCount = 32;

    public static readonly float[] SharedSampleBuffer = new float[CaptureSize];
    private static readonly float[] SampleBuffer = SharedSampleBuffer;
    private static readonly float[] FftRe = new float[FftSize];
    private static readonly float[] FftIm = new float[FftSize];
    private static readonly float[] Window = BuildHann(FftSize);
    private static readonly float[] SmoothedMag = new float[FftSize / 2 + 1];

    public static void Draw(SKCanvas canvas, VideoLayer layer, IVideoAudioScopeService scope,
        SKRect rect, double layerOpacity, AudioWaveform? staticWaveform = null, double playheadSeconds = 0)
    {
        if (layer.AudioSourceTrackId is not { } trackId) return;

        canvas.Save();
        canvas.ClipRect(rect);
        using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(layerOpacity * 255)) };
        canvas.SaveLayer(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), layerPaint);

        if (!layer.WaveformFollowPlayhead && staticWaveform is not null)
        {
            DrawStaticWaveform(canvas, layer, staticWaveform, rect);
            canvas.Restore();
            canvas.Restore();
            return;
        }

        var count = scope.CaptureLatest(trackId, SampleBuffer);
        if (count <= 0)
        {
            canvas.Restore();
            canvas.Restore();
            return;
        }

        switch (layer.WaveformStyle)
        {
            case VideoWaveformStyle.Bars:
                DrawVolumeBars(canvas, layer, SampleBuffer, count, rect);
                break;
            case VideoWaveformStyle.Spectrum:
                DrawSpectrum(canvas, layer, scope.GetSampleRate(trackId), SampleBuffer, count, rect);
                break;
            default:
                DrawOscilloscope(canvas, layer, SampleBuffer, count, rect);
                break;
        }

        canvas.Restore();
        canvas.Restore();
    }

    private static void DrawStaticWaveform(SKCanvas canvas, VideoLayer layer, AudioWaveform waveform, SKRect rect)
    {
        if (waveform.BucketCount <= 0 || waveform.TotalFrames <= 0) return;

        var points = Math.Max(8, (int)rect.Width);
        using var pen = CreateStrokePaint(layer, rect, (float)Math.Max(1, layer.SpectrumLineThickness));
        using var path = new SKPath();
        var started = false;
        for (var px = 0; px < points; px++)
        {
            var startFrame = (long)((double)px / points * waveform.TotalFrames);
            var endFrame = (long)((double)(px + 1) / points * waveform.TotalFrames);
            waveform.GetPeak(startFrame, endFrame, out _, out var peak);
            var x = rect.Left + px * rect.Width / points;
            var y = rect.MidY - peak * rect.Height * 0.45f;
            if (!started) { path.MoveTo(x, y); started = true; }
            else path.LineTo(x, y);
        }

        if (started)
            canvas.DrawPath(path, pen);
    }

    private static void DrawOscilloscope(SKCanvas canvas, VideoLayer layer, float[] samples, int count, SKRect rect)
    {
        var points = Math.Max(8, (int)rect.Width);
        var step = Math.Max(1, count / points);
        var midY = rect.Top + rect.Height * 0.5f;
        var amp = rect.Height * 0.45f;
        using var pen = CreateStrokePaint(layer, rect, (float)Math.Max(1, layer.SpectrumLineThickness));

        if (layer.WaveformStyle == VideoWaveformStyle.Mirrored)
        {
            using var path = new SKPath();
            var started = false;
            for (var px = 0; px < points; px++)
            {
                var idx = px * step;
                if (idx >= count) break;
                var x = rect.Left + px * rect.Width / points;
                var y = midY - samples[idx] * amp;
                if (!started) { path.MoveTo(x, y); started = true; }
                else path.LineTo(x, y);
            }

            for (var px = points - 1; px >= 0; px--)
            {
                var idx = px * step;
                if (idx >= count) continue;
                var x = rect.Left + px * rect.Width / points;
                path.LineTo(x, midY + samples[idx] * amp);
            }

            if (started)
            {
                path.Close();
                using var fill = CreateFillPaint(layer, rect);
                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, pen);
            }

            return;
        }

        using var linePath = new SKPath();
        var lineStarted = false;
        for (var px = 0; px < points; px++)
        {
            var idx = px * step;
            if (idx >= count) break;
            var x = rect.Left + px * rect.Width / points;
            var y = midY - samples[idx] * amp;
            if (!lineStarted) { linePath.MoveTo(x, y); lineStarted = true; }
            else linePath.LineTo(x, y);
        }

        if (lineStarted)
            canvas.DrawPath(linePath, pen);
    }

    private static void DrawVolumeBars(SKCanvas canvas, VideoLayer layer, float[] samples, int count, SKRect rect)
    {
        var barW = rect.Width / BarCount;
        var gap = Math.Min(2f, barW * 0.15f);
        var drawW = Math.Max(1f, barW - gap);
        var bucket = Math.Max(1, count / BarCount);

        for (var b = 0; b < BarCount; b++)
        {
            var start = b * bucket;
            var end = Math.Min(count, start + bucket);
            var peak = 0f;
            for (var i = start; i < end; i++)
                peak = Math.Max(peak, Math.Abs(samples[i]));

            var h = Math.Max(2f, peak * rect.Height);
            var barRect = new SKRect(rect.Left + b * barW + gap * 0.5f, rect.Bottom - h,
                rect.Left + b * barW + gap * 0.5f + drawW, rect.Bottom);
            using var paint = CreateBarPaint(layer, barRect, peak);
            canvas.DrawRect(barRect, paint);
        }
    }

    private static void DrawSpectrum(SKCanvas canvas, VideoLayer layer, int sampleRate, float[] samples, int count, SKRect rect)
    {
        var n = Math.Min(FftSize, count);
        if (n < 64) return;

        for (var i = 0; i < FftSize; i++)
        {
            FftRe[i] = i < n ? samples[i] * Window[i] : 0;
            FftIm[i] = 0;
        }

        Fft(FftRe, FftIm);

        var minHz = Math.Clamp(layer.SpectrumMinHz, 20, 20000);
        var maxHz = Math.Clamp(layer.SpectrumMaxHz, minHz + 10, 22000);
        var sr = sampleRate > 0 ? sampleRate : 44100;
        var bins = FftSize / 2;
        var scale = 4.0f / FftSize;

        for (var k = 0; k <= bins; k++)
        {
            var amp = MathF.Sqrt(FftRe[k] * FftRe[k] + FftIm[k] * FftIm[k]) * scale;
            var db = 20f * MathF.Log10(amp + 1e-9f);
            if (db < -84f) db = -84f;
            SmoothedMag[k] = db > SmoothedMag[k] ? db : SmoothedMag[k] + (db - SmoothedMag[k]) * 0.35f;
        }

        var thickness = (float)Math.Clamp(layer.SpectrumLineThickness, 0.5, 12);
        using var path = new SKPath();
        var started = false;
        for (var px = 0.0; px <= rect.Width; px += 1)
        {
            var t = rect.Width > 0 ? px / rect.Width : 0;
            var freq = minHz * Math.Pow(maxHz / minHz, t);
            var bin = (int)Math.Round(freq * FftSize / sr);
            if (bin < 0) bin = 0;
            else if (bin > bins) bin = bins;
            var norm = Math.Clamp((SmoothedMag[bin] + 84f) / 84f, 0, 1);
            var y = rect.Bottom - norm * rect.Height;
            var x = rect.Left + (float)px;
            if (!started) { path.MoveTo(x, y); started = true; }
            else path.LineTo(x, y);
        }

        if (started)
        {
            using var pen = CreateStrokePaint(layer, rect, thickness);
            canvas.DrawPath(path, pen);
        }
    }

    private static SKPaint CreateFillPaint(VideoLayer layer, SKRect rect)
    {
        var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        var shader = CreateColorShader(layer, rect, false);
        if (shader is not null) paint.Shader = shader;
        else paint.Color = ToSkColor(layer.WaveformColorArgb);
        return paint;
    }

    private static SKPaint CreateBarPaint(VideoLayer layer, SKRect barRect, float level)
    {
        var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        if (layer.VisualiserColorMode != VideoVisualiserColorMode.Gradient)
        {
            paint.Color = ToSkColor(layer.WaveformColorArgb);
            return paint;
        }

        var t = Math.Clamp(level, 0, 1);
        paint.Color = Interpolate(ToSkColor(layer.WaveformColorArgb), ToSkColor(layer.VisualiserColorSecondaryArgb), t);
        return paint;
    }

    private static SKPaint CreateStrokePaint(VideoLayer layer, SKRect rect, float thickness)
    {
        var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round
        };
        var shader = CreateColorShader(layer, rect, true);
        if (shader is not null) paint.Shader = shader;
        else paint.Color = ToSkColor(layer.WaveformColorArgb);
        return paint;
    }

    private static SKShader? CreateColorShader(VideoLayer layer, SKRect rect, bool diagonal)
    {
        if (layer.VisualiserColorMode != VideoVisualiserColorMode.Gradient)
            return null;

        var c1 = ToSkColor(layer.WaveformColorArgb);
        var c2 = ToSkColor(layer.VisualiserColorSecondaryArgb);
        return diagonal
            ? SKShader.CreateLinearGradient(new SKPoint(rect.Left, rect.Bottom), new SKPoint(rect.Right, rect.Top), new[] { c1, c2 }, SKShaderTileMode.Clamp)
            : SKShader.CreateLinearGradient(new SKPoint(rect.MidX, rect.Bottom), new SKPoint(rect.MidX, rect.Top), new[] { c1, c2 }, SKShaderTileMode.Clamp);
    }

    private static SKColor ToSkColor(uint argb) =>
        new((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));

    private static SKColor Interpolate(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * t),
            (byte)(a.Green + (b.Green - a.Green) * t),
            (byte)(a.Blue + (b.Blue - a.Blue) * t),
            (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
    }

    private static float[] BuildHann(int n)
    {
        var w = new float[n];
        for (var i = 0; i < n; i++)
            w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
        return w;
    }

    private static void Fft(float[] re, float[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2.0 * Math.PI / len;
            var wRe = (float)Math.Cos(ang);
            var wIm = (float)Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                float curRe = 1, curIm = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var a = i + k;
                    var b = a + len / 2;
                    var tre = re[b] * curRe - im[b] * curIm;
                    var tim = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tre;
                    im[b] = im[a] - tim;
                    re[a] += tre;
                    im[a] += tim;
                    var ncur = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = ncur;
                }
            }
        }
    }
}
