using SkiaSharp;

namespace Ongenet.App.Services;

internal static class SkiaCanvasText
{
    internal static void Draw(SKCanvas canvas, string text, float x, float y, SKColor color, float size,
        SKTypeface typeface)
    {
        using var font = new SKFont(typeface, size) { Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
    }
}
