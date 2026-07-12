using System;
using System.IO;
using System.Linq;
using Ongenet.Core.Models.Notation;
using SkiaSharp;

namespace Ongenet.App.Services;

/// <summary>Renders a <see cref="ScoreDocument"/> to a PDF file using SkiaSharp.</summary>
public static class StaffPdfExporter
{
    private const float LeftPad = 48f;
    private const float PageMargin = 36f;

    public static void Export(ScoreDocument doc, string path, double pixelsPerBeat = 48,
        float pageWidth = 842, float pageHeight = 595)
    {
        if (doc.Staves.Count == 0)
            throw new InvalidOperationException("No staves to export.");

        var leadSheet = doc.LayoutMode == ScoreLayoutMode.LeadSheet;
        var staffHeight = leadSheet ? 64f : 80f;
        var staffBlock = staffHeight + 24f;
        var maxBeat = doc.Staves.SelectMany(s => s.Notes).DefaultIfEmpty()
            .Max(n => n.StartBeat + n.LengthBeats);
        var contentWidth = Math.Max(pageWidth - PageMargin * 2,
            (float)(maxBeat * pixelsPerBeat) + LeftPad + 32f);
        _ = contentWidth;

        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var pdf = SKDocument.CreatePdf(stream);
        var canvas = pdf.BeginPage(pageWidth, pageHeight);

        using var linePaint = new SKPaint { Color = SKColors.Gray, StrokeWidth = 1, IsAntialias = true };
        using var notePaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var bgPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        using var chordPaint = new SKPaint
        {
            Color = new SKColor(0x88, 0x7c, 0xb0),
            TextSize = leadSheet ? 18f : 14f,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        canvas.DrawRect(0, 0, pageWidth, pageHeight, bgPaint);

        if (!string.IsNullOrWhiteSpace(doc.Title))
        {
            using var titlePaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = 20f,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
            };
            canvas.DrawText(doc.Title, PageMargin, PageMargin + 16, titlePaint);
        }

        var y = PageMargin + (string.IsNullOrWhiteSpace(doc.Title) ? 0 : 28f);
        foreach (var staff in doc.Staves)
        {
            if (y + staffBlock > pageHeight - PageMargin)
            {
                pdf.EndPage();
                canvas.Dispose();
                canvas = pdf.BeginPage(pageWidth, pageHeight);
                canvas.DrawRect(0, 0, pageWidth, pageHeight, bgPaint);
                y = PageMargin;
            }

            var top = y;
            var chordY = top - (leadSheet ? 6f : 14f);
            foreach (var sym in staff.ChordSymbols)
            {
                var cx = PageMargin + LeftPad + (float)(sym.StartBeat * pixelsPerBeat);
                canvas.DrawText(sym.Text, cx, chordY, chordPaint);
            }

            for (var line = 0; line < 5; line++)
            {
                var ly = top + line * (staffHeight / 4f);
                canvas.DrawLine(PageMargin + LeftPad, ly, pageWidth - PageMargin, ly, linePaint);
            }

            if (!leadSheet)
            {
                foreach (var n in staff.Notes)
                {
                    var x = PageMargin + LeftPad + (float)(n.StartBeat * pixelsPerBeat);
                    var pitchOffset = (72 - n.Pitch) * (staffHeight / 8f);
                    var ny = top + staffHeight / 2f + pitchOffset;
                    var w = Math.Max(6f, (float)(n.LengthBeats * pixelsPerBeat * 0.8));
                    canvas.DrawOval(x, ny - 5, w, 10, notePaint);
                }
            }
            else
            {
                foreach (var n in staff.Notes)
                {
                    var x = PageMargin + LeftPad + (float)(n.StartBeat * pixelsPerBeat);
                    var ny = top + staffHeight / 2f;
                    canvas.DrawCircle(x + 3, ny, 4f, notePaint);
                }
            }

            y += staffBlock;
        }

        foreach (var tuplet in doc.Tuplets)
        {
            var x1 = PageMargin + LeftPad + (float)(tuplet.StartBeat * pixelsPerBeat);
            var x2 = PageMargin + LeftPad + (float)((tuplet.StartBeat + tuplet.LengthBeats) * pixelsPerBeat);
            var ty = PageMargin + 8f;
            canvas.DrawLine(x1, ty, x2, ty, linePaint);
            using var tupPaint = new SKPaint
            {
                Color = new SKColor(0x88, 0x7c, 0xb0),
                TextSize = 11f,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial")
            };
            canvas.DrawText($"{tuplet.ActualNotes}:{tuplet.NormalNotes}", (x1 + x2) / 2 - 8, ty - 4, tupPaint);
        }

        pdf.EndPage();
        canvas.Dispose();
        pdf.Close();
    }
}
