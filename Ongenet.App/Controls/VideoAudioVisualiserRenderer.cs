using System;
using Avalonia;
using Avalonia.Media;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Controls;

/// <summary>Draws live audio visualisers on the video composition canvas.</summary>
public static class VideoAudioVisualiserRenderer
{
    private const int CaptureSize = 2048;
    private const int FftSize = 1024;
    private const int BarCount = 32;

    internal static readonly float[] SharedSampleBuffer = new float[CaptureSize];
    private static readonly float[] SampleBuffer = SharedSampleBuffer;
    private static readonly float[] FftRe = new float[FftSize];
    private static readonly float[] FftIm = new float[FftSize];
    private static readonly float[] Window = BuildHann(FftSize);
    private static readonly float[] SmoothedMag = new float[FftSize / 2 + 1];

    public static void Draw(DrawingContext ctx, VideoLayer layer, IVideoAudioScopeService scope,
        Rect rect, double layerOpacity)
    {
        if (layer.AudioSourceTrackId is not { } trackId) return;
        var count = scope.CaptureLatest(trackId, SampleBuffer);
        if (count <= 0) return;

        using (ctx.PushClip(rect))
        using (ctx.PushOpacity(layerOpacity))
        {
            switch (layer.WaveformStyle)
            {
                case VideoWaveformStyle.Bars:
                    DrawVolumeBars(ctx, layer, SampleBuffer, count, rect);
                    break;
                case VideoWaveformStyle.Spectrum:
                    DrawSpectrum(ctx, layer, scope.GetSampleRate(trackId), SampleBuffer, count, rect);
                    break;
                default:
                    DrawOscilloscope(ctx, layer, SampleBuffer, count, rect);
                    break;
            }
        }
    }

    private static void DrawOscilloscope(DrawingContext ctx, VideoLayer layer, float[] samples, int count, Rect rect)
    {
        var points = Math.Max(8, (int)rect.Width);
        var step = Math.Max(1, count / points);
        var midY = rect.Y + rect.Height * 0.5;
        var amp = rect.Height * 0.45;
        var pen = CreatePen(layer, rect, Math.Max(1, layer.SpectrumLineThickness));

        if (layer.WaveformStyle == VideoWaveformStyle.Mirrored)
        {
            var fillGeo = new StreamGeometry();
            using (var g = fillGeo.Open())
            {
                var started = false;
                for (var px = 0; px < points; px++)
                {
                    var idx = px * step;
                    if (idx >= count) break;
                    var x = rect.X + px * rect.Width / points;
                    var y = midY - samples[idx] * amp;
                    if (!started) { g.BeginFigure(new Point(x, y), false); started = true; }
                    else g.LineTo(new Point(x, y));
                }

                for (var px = points - 1; px >= 0; px--)
                {
                    var idx = px * step;
                    if (idx >= count) continue;
                    var x = rect.X + px * rect.Width / points;
                    g.LineTo(new Point(x, midY + samples[idx] * amp));
                }

                if (started) g.EndFigure(true);
            }

            ctx.DrawGeometry(CreateFillBrush(layer, rect), pen, fillGeo);
            return;
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var started = false;
            for (var px = 0; px < points; px++)
            {
                var idx = px * step;
                if (idx >= count) break;
                var x = rect.X + px * rect.Width / points;
                var y = midY - samples[idx] * amp;
                if (!started) { g.BeginFigure(new Point(x, y), false); started = true; }
                else g.LineTo(new Point(x, y));
            }

            if (started) g.EndFigure(false);
        }

        ctx.DrawGeometry(null, pen, geometry);
    }

    private static void DrawVolumeBars(DrawingContext ctx, VideoLayer layer, float[] samples, int count, Rect rect)
    {
        var barW = rect.Width / BarCount;
        var gap = Math.Min(2, barW * 0.15);
        var drawW = Math.Max(1, barW - gap);
        var bucket = Math.Max(1, count / BarCount);

        for (var b = 0; b < BarCount; b++)
        {
            var start = b * bucket;
            var end = Math.Min(count, start + bucket);
            float peak = 0;
            for (var i = start; i < end; i++)
                peak = Math.Max(peak, Math.Abs(samples[i]));

            var h = Math.Max(2, peak * rect.Height);
            var barRect = new Rect(rect.X + b * barW + gap * 0.5, rect.Bottom - h, drawW, h);
            ctx.DrawRectangle(CreateBarBrush(layer, barRect, peak), null, barRect);
        }
    }

    private static void DrawSpectrum(DrawingContext ctx, VideoLayer layer, int sampleRate, float[] samples, int count, Rect rect)
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

        var thickness = Math.Clamp(layer.SpectrumLineThickness, 0.5, 12);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
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
                var x = rect.X + px;
                if (!started) { g.BeginFigure(new Point(x, y), false); started = true; }
                else g.LineTo(new Point(x, y));
            }

            if (started) g.EndFigure(false);
        }

        ctx.DrawGeometry(null, CreatePen(layer, rect, thickness), geometry);
    }

    private static IBrush CreateFillBrush(VideoLayer layer, Rect rect)
    {
        if (layer.VisualiserColorMode == VideoVisualiserColorMode.Gradient)
        {
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(ToColor(layer.WaveformColorArgb), 0),
                    new GradientStop(ToColor(layer.VisualiserColorSecondaryArgb), 1)
                }
            };
        }

        return new SolidColorBrush(ToColor(layer.WaveformColorArgb));
    }

    private static IBrush CreateBarBrush(VideoLayer layer, Rect barRect, float level)
    {
        if (layer.VisualiserColorMode != VideoVisualiserColorMode.Gradient)
            return new SolidColorBrush(ToColor(layer.WaveformColorArgb));

        var t = Math.Clamp(level, 0, 1);
        var c1 = ToColor(layer.WaveformColorArgb);
        var c2 = ToColor(layer.VisualiserColorSecondaryArgb);
        return new SolidColorBrush(Interpolate(c1, c2, t));
    }

    private static IPen CreatePen(VideoLayer layer, Rect rect, double thickness)
    {
        if (layer.VisualiserColorMode == VideoVisualiserColorMode.Gradient)
        {
            return new Pen(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(ToColor(layer.WaveformColorArgb), 0),
                    new GradientStop(ToColor(layer.VisualiserColorSecondaryArgb), 1)
                }
            }, thickness);
        }

        return new Pen(new SolidColorBrush(ToColor(layer.WaveformColorArgb)), thickness);
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));

    private static Color Interpolate(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
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
