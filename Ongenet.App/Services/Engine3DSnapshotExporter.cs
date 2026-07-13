using System;
using System.IO;
using System.Threading;
using Ongenet.App.Controls.Engine3D;
using Ongenet.Engine3D.Abstractions;
using SkiaSharp;

namespace Ongenet.App.Services;

/// <summary>One-shot offscreen Engine3D render to a PNG beside the project.</summary>
public static class Engine3DSnapshotExporter
{
    public static string? Export(I3DEngineFactory factory, Scene scene, int width, int height, string? projectDirectory)
    {
        if (!factory.IsAvailable) return null;

        width = Math.Clamp(width, 16, 4096);
        height = Math.Clamp(height, 16, 4096);

        var session = factory.CreateSession(width, height);
        if (session is null) return null;

        using var loop = new Engine3DRenderLoop(session);
        var snapshot = SceneSnapshot.Capture(scene);
        loop.Submit(snapshot, width, height);

        FrameBuffer? frame = null;
        for (var i = 0; i < 80; i++)
        {
            frame = loop.AcquireFrame();
            if (frame is not null) break;
            Thread.Sleep(25);
        }

        if (frame is null) return null;

        try
        {
            var dir = !string.IsNullOrWhiteSpace(projectDirectory)
                ? Path.Combine(projectDirectory, ".ongenet", "engine3d-snapshots")
                : Path.Combine(Path.GetTempPath(), "ongenet-engine3d-snapshots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            using var bitmap = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            var rowBytes = frame.Width * 4;
            var dstPtr = bitmap.GetPixels();
            for (var y = 0; y < frame.Height; y++)
            {
                var srcOffset = y * frame.Stride;
                if (srcOffset + rowBytes > frame.Pixels.Length) break;
                System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, srcOffset, dstPtr + y * bitmap.RowBytes, rowBytes);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            if (data is null) return null;
            using (var fs = File.Create(path))
                data.SaveTo(fs);
            return path;
        }
        finally
        {
            loop.ReleaseFrame();
        }
    }
}
