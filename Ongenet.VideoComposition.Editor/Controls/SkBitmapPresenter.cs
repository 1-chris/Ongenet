using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Ongenet.VideoComposition.Editor.Controls;

internal sealed class SkBitmapPresenter : IDisposable
{
    private WriteableBitmap? _bitmap;
    private int _width;
    private int _height;

    public WriteableBitmap? Present(SKBitmap source)
    {
        if (source.Width <= 0 || source.Height <= 0) return null;
        EnsureBitmap(source.Width, source.Height);

        using var locked = _bitmap!.Lock();
        var copyBytes = Math.Min(source.RowBytes, locked.RowBytes);
        var srcPtr = source.GetPixels();
        var row = new byte[copyBytes];
        for (var y = 0; y < source.Height; y++)
        {
            Marshal.Copy(srcPtr + y * source.RowBytes, row, 0, copyBytes);
            Marshal.Copy(row, 0, locked.Address + y * locked.RowBytes, copyBytes);
        }

        return _bitmap;
    }

    private void EnsureBitmap(int width, int height)
    {
        if (_bitmap is not null && _width == width && _height == height) return;
        _bitmap?.Dispose();
        _width = width;
        _height = height;
        _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
    }

    public void Dispose() => _bitmap?.Dispose();
}
