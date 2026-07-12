using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Ongenet.App.Theming;
using Ongenet.Core.Models.Notation;
using SkiaSharp;

namespace Ongenet.App.Controls;

/// <summary>Skia-rendered staff notation from a <see cref="ScoreDocument"/> with pointer editing.</summary>
public sealed class StaffControl : ThemedControl
{
    public static readonly StyledProperty<ScoreDocument?> DocumentProperty =
        AvaloniaProperty.Register<StaffControl, ScoreDocument?>(nameof(Document));

    public static readonly StyledProperty<double> PixelsPerBeatProperty =
        AvaloniaProperty.Register<StaffControl, double>(nameof(PixelsPerBeat), 48.0);

    public static readonly StyledProperty<ScoreNote?> SelectedNoteProperty =
        AvaloniaProperty.Register<StaffControl, ScoreNote?>(nameof(SelectedNote));

    static StaffControl()
    {
        AffectsRender<StaffControl>(DocumentProperty, PixelsPerBeatProperty, SelectedNoteProperty);
    }

    public ScoreDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public double PixelsPerBeat
    {
        get => GetValue(PixelsPerBeatProperty);
        set => SetValue(PixelsPerBeatProperty, value);
    }

    public ScoreNote? SelectedNote
    {
        get => GetValue(SelectedNoteProperty);
        set => SetValue(SelectedNoteProperty, value);
    }

    /// <summary>Raised when pointer editing completes (move or resize).</summary>
    public event Action? ScoreEdited;

    private SKColor _lineColor = SKColors.Gray;
    private SKColor _noteColor = SKColors.White;
    private SKColor _selectedNoteColor = SKColors.Cyan;
    private SKColor _chordColor = SKColors.White;
    private SKColor _bgColor = SKColors.Black;

    private enum DragMode { None, Move, Resize }

    private DragMode _drag = DragMode.None;
    private ScoreNote? _dragNote;
    private double _dragStartBeat;
    private int _dragStartPitch;
    private double _dragStartLength;
    private Point _dragStartPoint;

    public StaffControl()
    {
        Focusable = true;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    protected override void BuildThemeResources()
    {
        _lineColor = ToSkia(ThemePalette.Overlay1);
        _noteColor = ToSkia(ThemePalette.Text);
        _selectedNoteColor = ToSkia(ThemePalette.Sky);
        _chordColor = ToSkia(ThemePalette.Mauve);
        _bgColor = ToSkia(ThemePalette.Base);
    }

    public override void Render(DrawingContext context)
    {
        if (Document is null || Document.Staves.Count == 0)
        {
            base.Render(context);
            return;
        }

        var bounds = Bounds;
        context.Custom(new StaffDrawOperation(new Rect(bounds.Size), Document, PixelsPerBeat,
            SelectedNote, _lineColor, _noteColor, _selectedNoteColor, _chordColor, _bgColor));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Document is null || Document.LayoutMode == ScoreLayoutMode.LeadSheet) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);
        if (!TryHitTest(pos, out var note, out var resizeEdge))
            return;

        SelectedNote = note;
        _dragNote = note;
        _drag = resizeEdge ? DragMode.Resize : DragMode.Move;
        _dragStartBeat = note.StartBeat;
        _dragStartPitch = note.Pitch;
        _dragStartLength = note.LengthBeats;
        _dragStartPoint = pos;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag == DragMode.None || _dragNote is null || Document is null) return;

        var pos = e.GetPosition(this);
        var deltaX = pos.X - _dragStartPoint.X;
        var deltaY = pos.Y - _dragStartPoint.Y;

        if (_drag == DragMode.Move)
        {
            var beatDelta = deltaX / PixelsPerBeat;
            _dragNote.StartBeat = Math.Max(0, _dragStartBeat + beatDelta);
            var semis = -(int)Math.Round(deltaY / 8.0);
            _dragNote.Pitch = Math.Clamp(_dragStartPitch + semis, 0, 127);
        }
        else
        {
            var newLen = _dragStartLength + deltaX / PixelsPerBeat;
            _dragNote.LengthBeats = Math.Max(0.25, newLen);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag == DragMode.None || _dragNote is null) return;
        _drag = DragMode.None;
        _dragNote = null;
        e.Pointer.Capture(null);
        ScoreEdited?.Invoke();
        e.Handled = true;
    }

    private bool TryHitTest(Point pos, out ScoreNote note, out bool resizeEdge)
    {
        note = null!;
        resizeEdge = false;
        if (Document is null) return false;

        var leadSheet = Document.LayoutMode == ScoreLayoutMode.LeadSheet;
        if (leadSheet) return false;

        const float leftPad = 48f;
        var staffHeight = 80f;
        var y = 16f;

        foreach (var staff in Document.Staves)
        {
            var top = y;
            foreach (var n in staff.Notes)
            {
                var x = leftPad + (float)(n.StartBeat * PixelsPerBeat);
                var pitchOffset = (72 - n.Pitch) * (staffHeight / 8f);
                var ny = top + staffHeight / 2f + pitchOffset;
                var w = Math.Max(6f, (float)(n.LengthBeats * PixelsPerBeat * 0.8));

                var rect = new Rect(x, ny - 8, w, 16);
                if (!rect.Contains(pos)) continue;

                note = n;
                resizeEdge = pos.X >= x + w - 8;
                return true;
            }

            y += staffHeight + 24f;
        }

        return false;
    }

    private static SKColor ToSkia(Color c) => new(c.R, c.G, c.B, c.A);

    private sealed class StaffDrawOperation : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly ScoreDocument _doc;
        private readonly double _ppb;
        private readonly ScoreNote? _selected;
        private readonly SKColor _line;
        private readonly SKColor _note;
        private readonly SKColor _selectedNote;
        private readonly SKColor _chord;
        private readonly SKColor _bg;

        public StaffDrawOperation(Rect bounds, ScoreDocument doc, double ppb, ScoreNote? selected,
            SKColor line, SKColor note, SKColor selectedNote, SKColor chord, SKColor bg)
        {
            _bounds = bounds;
            _doc = doc;
            _ppb = ppb;
            _selected = selected;
            _line = line;
            _note = note;
            _selectedNote = selectedNote;
            _chord = chord;
            _bg = bg;
        }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            using var lease = leaseFeature?.Lease();
            var canvas = lease?.SkCanvas;
            if (canvas is null) return;

            canvas.Save();
            canvas.ClipRect(SKRect.Create(0, 0, (float)_bounds.Width, (float)_bounds.Height));

            var leadSheet = _doc.LayoutMode == ScoreLayoutMode.LeadSheet;
            var staffHeight = leadSheet ? 64f : 80f;
            var leftPad = 48f;
            var y = 16f;

            using var linePaint = new SKPaint { Color = _line, StrokeWidth = 1, IsAntialias = true };
            using var notePaint = new SKPaint { Color = _note, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var selectedPaint = new SKPaint { Color = _selectedNote, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var bgPaint = new SKPaint { Color = _bg, Style = SKPaintStyle.Fill };
            using var chordPaint = new SKPaint
            {
                Color = _chord,
                TextSize = leadSheet ? 18f : 14f,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Inter", SKFontStyle.Bold)
            };

            canvas.DrawRect(0, 0, (float)_bounds.Width, (float)_bounds.Height, bgPaint);

            foreach (var staff in _doc.Staves)
            {
                var top = y;
                var chordY = top - (leadSheet ? 6f : 14f);

                foreach (var sym in staff.ChordSymbols)
                {
                    var cx = leftPad + (float)(sym.StartBeat * _ppb);
                    canvas.DrawText(sym.Text, cx, chordY, chordPaint);
                }

                for (var line = 0; line < 5; line++)
                {
                    var ly = top + line * (staffHeight / 4f);
                    canvas.DrawLine(leftPad, ly, (float)_bounds.Width - 8, ly, linePaint);
                }

                if (!leadSheet)
                {
                    foreach (var n in staff.Notes)
                    {
                        var x = leftPad + (float)(n.StartBeat * _ppb);
                        var pitchOffset = (72 - n.Pitch) * (staffHeight / 8f);
                        var ny = top + staffHeight / 2f + pitchOffset;
                        var w = Math.Max(6f, (float)(n.LengthBeats * _ppb * 0.8));
                        var paint = ReferenceEquals(n, _selected) ? selectedPaint : notePaint;
                        canvas.DrawOval(x, ny - 5, w, 10, paint);

                        if (n.Articulation != ScoreArticulation.None)
                        {
                            using var artPaint = new SKPaint
                            {
                                Color = _note,
                                TextSize = 10f,
                                IsAntialias = true,
                                Typeface = SKTypeface.FromFamilyName("Inter")
                            };
                            var sym = n.Articulation switch
                            {
                                ScoreArticulation.Staccato => "·",
                                ScoreArticulation.Accent => ">",
                                ScoreArticulation.Tenuto => "—",
                                ScoreArticulation.Marcato => "^",
                                ScoreArticulation.Legato => "⌒",
                                _ => ""
                            };
                            if (sym.Length > 0)
                                canvas.DrawText(sym, x, ny - 12, artPaint);
                        }

                        if (n.Dynamic != ScoreDynamic.None)
                        {
                            using var dynPaint = new SKPaint
                            {
                                Color = _line,
                                TextSize = 9f,
                                IsAntialias = true,
                                Typeface = SKTypeface.FromFamilyName("Inter", SKFontStyle.Italic)
                            };
                            var dyn = n.Dynamic switch
                            {
                                ScoreDynamic.Ppp => "ppp",
                                ScoreDynamic.Pp => "pp",
                                ScoreDynamic.P => "p",
                                ScoreDynamic.Mp => "mp",
                                ScoreDynamic.Mf => "mf",
                                ScoreDynamic.F => "f",
                                ScoreDynamic.Ff => "ff",
                                ScoreDynamic.Fff => "fff",
                                _ => ""
                            };
                            if (dyn.Length > 0)
                                canvas.DrawText(dyn, x, top + staffHeight + 10, dynPaint);
                        }
                    }
                }
                else
                {
                    foreach (var n in staff.Notes)
                    {
                        var x = leftPad + (float)(n.StartBeat * _ppb);
                        var ny = top + staffHeight / 2f;
                        canvas.DrawCircle(x + 3, ny, 4f, notePaint);
                    }
                }

                y += staffHeight + 24f;
            }

            foreach (var tuplet in _doc.Tuplets)
            {
                var x1 = leftPad + (float)(tuplet.StartBeat * _ppb);
                var x2 = leftPad + (float)((tuplet.StartBeat + tuplet.LengthBeats) * _ppb);
                var ty = 12f;
                canvas.DrawLine(x1, ty, x2, ty, linePaint);
                using var tupPaint = new SKPaint
                {
                    Color = _chord,
                    TextSize = 11f,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Inter")
                };
                canvas.DrawText($"{tuplet.ActualNotes}:{tuplet.NormalNotes}", (x1 + x2) / 2 - 8, ty - 4, tupPaint);
            }

            canvas.Restore();
        }

        public Rect Bounds => _bounds;
        public bool HitTest(Point p) => _bounds.Contains(p);
        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => false;
    }
}
