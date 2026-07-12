using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Ongenet.App.Theming;
using Ongenet.App.ViewModels;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.App.Controls;

/// <summary>Waveform with polyphonic pitch segment overlays and vertical drag to adjust cents.</summary>
public sealed class PolyphonicPitchEditorControl : ThemedControl
{
    public static readonly StyledProperty<AudioWaveform?> WaveformProperty =
        AvaloniaProperty.Register<PolyphonicPitchEditorControl, AudioWaveform?>(nameof(Waveform));

    public static readonly StyledProperty<long> TotalFramesProperty =
        AvaloniaProperty.Register<PolyphonicPitchEditorControl, long>(nameof(TotalFrames));

    public static readonly StyledProperty<IReadOnlyList<PitchNoteSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<PolyphonicPitchEditorControl, IReadOnlyList<PitchNoteSegment>?>(nameof(Segments));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<PolyphonicPitchEditorControl, int>(nameof(SelectedIndex), -1);

    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<PolyphonicPitchEditorControl, int>(nameof(Revision));

    private IBrush _segmentFill = Brushes.Transparent;
    private IBrush _selectedFill = Brushes.Transparent;
    private IBrush _waveFill = Brushes.Gray;
    private IPen _segmentStroke = new Pen(Brushes.White, 1);

    private bool _draggingPitch;
    private double _dragStartY;
    private double _dragStartCents;

    static PolyphonicPitchEditorControl()
    {
        AffectsRender<PolyphonicPitchEditorControl>(WaveformProperty, TotalFramesProperty, SegmentsProperty,
            SelectedIndexProperty, RevisionProperty);
    }

    public AudioWaveform? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    public long TotalFrames
    {
        get => GetValue(TotalFramesProperty);
        set => SetValue(TotalFramesProperty, value);
    }

    public IReadOnlyList<PitchNoteSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public int Revision
    {
        get => GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    protected override void BuildThemeResources()
    {
        _segmentFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Mauve, 90));
        _selectedFill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Mauve, 160));
        _waveFill = ThemePalette.BrushOf("Overlay0");
        _segmentStroke = new Pen(ThemePalette.BrushOf("Mauve"), 1);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is not PolyphonicPitchEditorViewModel vm) return;
        var pos = e.GetPosition(this);
        var idx = HitTestSegment(pos.X);
        if (idx >= 0 && idx < vm.Segments.Count)
        {
            vm.SelectedSegment = vm.Segments[idx];
            _draggingPitch = true;
            _dragStartY = pos.Y;
            _dragStartCents = vm.SelectedSegment.PitchCents;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_draggingPitch || DataContext is not PolyphonicPitchEditorViewModel vm || vm.SelectedSegment is null)
            return;

        var pos = e.GetPosition(this);
        var deltaY = _dragStartY - pos.Y;
        var cents = Math.Clamp(_dragStartCents + deltaY * 2.0, -2400, 2400);
        vm.SelectedSegment.PitchCents = cents;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_draggingPitch) return;
        _draggingPitch = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private int HitTestSegment(double x)
    {
        var segments = Segments;
        var total = TotalFrames;
        if (segments is null || total <= 0 || Bounds.Width <= 0) return -1;

        var frame = (long)(x / Bounds.Width * total);
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (frame >= seg.StartSample && frame < seg.EndSample) return i;
        }

        return -1;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(ThemePalette.BrushOf("Crust"), bounds);

        var waveform = Waveform;
        var total = TotalFrames > 0 ? TotalFrames : waveform?.TotalFrames ?? 0;
        if (total <= 0) return;

        var w = bounds.Width;
        var h = bounds.Height;
        var mid = h * 0.5;

        if (waveform is not null)
        {
            var cols = Math.Max(1, (int)w);
            for (var col = 0; col < cols; col++)
            {
                var startF = (long)(col / w * total);
                var endF = (long)((col + 1) / w * total);
                if (endF <= startF) endF = startF + 1;
                waveform.GetPeak(startF, endF, out var min, out var max);
                var y0 = mid - max * mid * 0.9;
                var y1 = mid - min * mid * 0.9;
                context.DrawLine(new Pen(_waveFill, 1), new Point(col, y0), new Point(col, y1));
            }
        }

        var segments = Segments;
        if (segments is null) return;

        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var x0 = seg.StartSample / (double)total * w;
            var x1 = seg.EndSample / (double)total * w;
            var rect = new Rect(x0, 0, Math.Max(1, x1 - x0), h);
            var fill = i == SelectedIndex ? _selectedFill : _segmentFill;
            context.FillRectangle(fill, rect);
            context.DrawRectangle(_segmentStroke, rect);

            var label = $"{seg.PitchCents:0}¢";
            var formatted = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, 10, Brushes.White);
            context.DrawText(formatted, new Point(x0 + 2, 2));
        }
    }
}
