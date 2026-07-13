using System;
using System.Collections.Generic;
using System.IO;
using Ongenet.Core.Models.Media;
using SkiaSharp;

namespace Ongenet.VideoComposition.Rendering;

/// <summary>Skia image effects: chroma key, color grading, masks, blend modes.</summary>
public static class VideoCompositionEffects
{
    private static readonly Dictionary<string, float[]> LutCache = new(StringComparer.OrdinalIgnoreCase);

    public static SKBlendMode ToSkBlendMode(VideoBlendMode mode) => mode switch
    {
        VideoBlendMode.Multiply => SKBlendMode.Multiply,
        VideoBlendMode.Screen => SKBlendMode.Screen,
        VideoBlendMode.Overlay => SKBlendMode.Overlay,
        _ => SKBlendMode.SrcOver
    };

    public static SKBitmap ApplyItemEffects(SKBitmap source, VideoLayerItem item, VideoCompositionExportAssets assets)
    {
        var working = source;
        if (item.ChromaKeyEnabled)
            working = ApplyChromaKey(working, item);

        if (Math.Abs(item.Brightness - 1) > 1e-3 || Math.Abs(item.Contrast - 1) > 1e-3
            || Math.Abs(item.Saturation - 1) > 1e-3)
            working = ApplyColorGrade(working, item);

        if (!string.IsNullOrWhiteSpace(item.LutCubePath) && File.Exists(item.LutCubePath))
            working = ApplyLut(working, item.LutCubePath);

        if (!string.IsNullOrWhiteSpace(item.MaskImagePath))
        {
            var mask = assets.GetImage(item.MaskImagePath);
            if (mask is not null)
                working = ApplyMask(working, mask);
        }

        return working;
    }

    private static SKBitmap ApplyChromaKey(SKBitmap source, VideoLayerItem item)
    {
        var result = source.Copy();
        var key = ToSkColor(item.ChromaKeyColorArgb);
        var tol = item.ChromaKeyTolerance;
        var feather = Math.Max(1e-4, item.ChromaKeyFeather);
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var c = result.GetPixel(x, y);
                var dist = ColorDistance(c, key);
                if (dist <= tol)
                    result.SetPixel(x, y, SKColors.Transparent);
                else if (dist <= tol + feather)
                {
                    var alpha = (byte)((dist - tol) / feather * c.Alpha);
                    result.SetPixel(x, y, c.WithAlpha(alpha));
                }
            }
        }

        return result;
    }

    private static SKBitmap ApplyColorGrade(SKBitmap source, VideoLayerItem item)
    {
        var result = source.Copy();
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var c = result.GetPixel(x, y);
                var r = ClampByte((c.Red - 128) * item.Contrast + 128 + (item.Brightness - 1) * 128);
                var g = ClampByte((c.Green - 128) * item.Contrast + 128 + (item.Brightness - 1) * 128);
                var b = ClampByte((c.Blue - 128) * item.Contrast + 128 + (item.Brightness - 1) * 128);
                if (Math.Abs(item.Saturation - 1) > 1e-3)
                {
                    var gray = 0.299 * r + 0.587 * g + 0.114 * b;
                    r = ClampByte(gray + (r - gray) * item.Saturation);
                    g = ClampByte(gray + (g - gray) * item.Saturation);
                    b = ClampByte(gray + (b - gray) * item.Saturation);
                }

                result.SetPixel(x, y, new SKColor((byte)r, (byte)g, (byte)b, c.Alpha));
            }
        }

        return result;
    }

    private static SKBitmap ApplyMask(SKBitmap source, SKBitmap mask)
    {
        var result = source.Copy();
        var mw = mask.Width;
        var mh = mask.Height;
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var mx = x * mw / Math.Max(1, result.Width);
                var my = y * mh / Math.Max(1, result.Height);
                var m = mask.GetPixel(Math.Clamp(mx, 0, mw - 1), Math.Clamp(my, 0, mh - 1));
                var c = result.GetPixel(x, y);
                result.SetPixel(x, y, c.WithAlpha((byte)(c.Alpha * m.Red / 255.0)));
            }
        }

        return result;
    }

    private static SKBitmap ApplyLut(SKBitmap source, string cubePath)
    {
        if (!LutCache.TryGetValue(cubePath, out var lut))
        {
            lut = ParseCubeLut(cubePath);
            LutCache[cubePath] = lut;
        }

        if (lut.Length == 0) return source;
        var size = (int)Math.Round(Math.Pow(lut.Length / 3, 1.0 / 3.0));
        if (size < 2) return source;

        var result = source.Copy();
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var c = result.GetPixel(x, y);
                var r = SampleLut(lut, size, c.Red / 255f, c.Green / 255f, c.Blue / 255f);
                result.SetPixel(x, y, new SKColor(
                    (byte)(r.R * 255), (byte)(r.G * 255), (byte)(r.B * 255), c.Alpha));
            }
        }

        return result;
    }

    private static (float R, float G, float B) SampleLut(float[] lut, int size, float r, float g, float b)
    {
        var ri = Math.Clamp((int)(r * (size - 1)), 0, size - 1);
        var gi = Math.Clamp((int)(g * (size - 1)), 0, size - 1);
        var bi = Math.Clamp((int)(b * (size - 1)), 0, size - 1);
        var idx = (bi * size * size + gi * size + ri) * 3;
        if (idx + 2 >= lut.Length) return (r, g, b);
        return (lut[idx], lut[idx + 1], lut[idx + 2]);
    }

    private static float[] ParseCubeLut(string path)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            var size = 0;
            var values = new List<float>();
            foreach (var line in lines)
            {
                if (line.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) int.TryParse(parts[1], out size);
                }
                else if (line.Length > 0 && char.IsDigit(line[0]))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3
                        && float.TryParse(parts[0], out var r)
                        && float.TryParse(parts[1], out var g)
                        && float.TryParse(parts[2], out var b))
                    {
                        values.Add(r);
                        values.Add(g);
                        values.Add(b);
                    }
                }
            }

            return values.Count > 0 ? values.ToArray() : Array.Empty<float>();
        }
        catch
        {
            return Array.Empty<float>();
        }
    }

    private static double ColorDistance(SKColor a, SKColor b)
    {
        var dr = (a.Red - b.Red) / 255.0;
        var dg = (a.Green - b.Green) / 255.0;
        var db = (a.Blue - b.Blue) / 255.0;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static SKColor ToSkColor(uint argb) =>
        new((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));

    private static int ClampByte(double v) => (int)Math.Clamp(v, 0, 255);
}
