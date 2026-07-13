using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ongenet.Engine3D.Abstractions;
using SkiaSharp;

namespace Ongenet.App.Controls.Engine3D;

/// <summary>CPU-bakes an image onto a cube mesh (per-vertex colour sampling) for video 3D FX layers.</summary>
internal static class TexturedMeshBuilder
{
    private const int FaceSubdivisions = 24;

    public static MeshData CreateTexturedBox(string? imagePath, float size = 1.2f)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return MeshData.Box(size);

        try
        {
            using var source = SKBitmap.Decode(imagePath);
            if (source is null) return MeshData.Box(size);

            using var faceTexture = BakeStretchedFaceTexture(source, FaceSubdivisions + 1);
            var h = size * 0.5f;
            var verts = new List<Vertex>(FaceSubdivisions * FaceSubdivisions * 6 * 4);
            var idx = new List<uint>(FaceSubdivisions * FaceSubdivisions * 6 * 6);

            void Face(Vector3 normal, Vector3 origin, Vector3 axisU, Vector3 axisV)
            {
                for (var j = 0; j < FaceSubdivisions; j++)
                {
                    for (var i = 0; i < FaceSubdivisions; i++)
                    {
                        var u0 = i / (float)FaceSubdivisions;
                        var u1 = (i + 1) / (float)FaceSubdivisions;
                        var v0 = j / (float)FaceSubdivisions;
                        var v1 = (j + 1) / (float)FaceSubdivisions;

                        var a = origin + axisU * (u0 * 2f - 1f) * h + axisV * (v0 * 2f - 1f) * h;
                        var b = origin + axisU * (u1 * 2f - 1f) * h + axisV * (v0 * 2f - 1f) * h;
                        var c = origin + axisU * (u1 * 2f - 1f) * h + axisV * (v1 * 2f - 1f) * h;
                        var d = origin + axisU * (u0 * 2f - 1f) * h + axisV * (v1 * 2f - 1f) * h;

                        var start = (uint)verts.Count;
                        verts.Add(MakeVertex(a, normal, faceTexture, u0, v1));
                        verts.Add(MakeVertex(b, normal, faceTexture, u1, v1));
                        verts.Add(MakeVertex(c, normal, faceTexture, u1, v0));
                        verts.Add(MakeVertex(d, normal, faceTexture, u0, v0));
                        idx.Add(start); idx.Add(start + 1); idx.Add(start + 2);
                        idx.Add(start); idx.Add(start + 2); idx.Add(start + 3);
                    }
                }
            }

            Face(new Vector3(0, 0, 1), new Vector3(0, 0, h), Vector3.UnitX, Vector3.UnitY);
            Face(new Vector3(0, 0, -1), new Vector3(0, 0, -h), -Vector3.UnitX, Vector3.UnitY);
            Face(new Vector3(1, 0, 0), new Vector3(h, 0, 0), -Vector3.UnitZ, Vector3.UnitY);
            Face(new Vector3(-1, 0, 0), new Vector3(-h, 0, 0), Vector3.UnitZ, Vector3.UnitY);
            Face(new Vector3(0, 1, 0), new Vector3(0, h, 0), Vector3.UnitX, -Vector3.UnitZ);
            Face(new Vector3(0, -1, 0), new Vector3(0, -h, 0), Vector3.UnitX, Vector3.UnitZ);

            return new MeshData(verts.ToArray(), idx.ToArray());
        }
        catch
        {
            return MeshData.Box(size);
        }
    }

    /// <summary>Stretch the full source image to a square face texture.</summary>
    private static SKBitmap BakeStretchedFaceTexture(SKBitmap source, int resolution)
    {
        var baked = new SKBitmap(resolution, resolution, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(baked);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };
        canvas.DrawBitmap(source, new SKRect(0, 0, resolution, resolution), paint);
        canvas.Flush();
        return baked;
    }

    private static Vertex MakeVertex(Vector3 pos, Vector3 normal, SKBitmap bitmap, float u, float v)
    {
        var color = SampleBilinear(bitmap, u, v);
        return new Vertex(pos, normal, color);
    }

    private static Vector4 SampleBilinear(SKBitmap bitmap, float u, float v)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return Vector4.One;

        var fx = Math.Clamp(u, 0f, 1f) * (bitmap.Width - 1);
        var fy = Math.Clamp(1f - v, 0f, 1f) * (bitmap.Height - 1);
        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        var x1 = Math.Min(x0 + 1, bitmap.Width - 1);
        var y1 = Math.Min(y0 + 1, bitmap.Height - 1);
        var tx = fx - x0;
        var ty = fy - y0;

        var c00 = PixelToVector4(bitmap.GetPixel(x0, y0));
        var c10 = PixelToVector4(bitmap.GetPixel(x1, y0));
        var c01 = PixelToVector4(bitmap.GetPixel(x0, y1));
        var c11 = PixelToVector4(bitmap.GetPixel(x1, y1));

        var top = Vector4.Lerp(c00, c10, tx);
        var bot = Vector4.Lerp(c01, c11, tx);
        return Vector4.Lerp(top, bot, ty);
    }

    private static Vector4 PixelToVector4(SKColor c) =>
        new(c.Red / 255f, c.Green / 255f, c.Blue / 255f, c.Alpha / 255f);
}
