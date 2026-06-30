using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Ongenet.App.Theming;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// Draws the timeline's vertical grid behind a lane's clips: strong bar lines, medium beat
    /// lines, and faint sub-beat lines, with the sub-beat density following the zoom.
    /// </summary>
    public sealed class TimelineGridControl : ThemedControl
    {
        public static readonly StyledProperty<double> PixelsPerBeatProperty =
            AvaloniaProperty.Register<TimelineGridControl, double>(nameof(PixelsPerBeat));

        public static readonly StyledProperty<double> TotalBeatsProperty =
            AvaloniaProperty.Register<TimelineGridControl, double>(nameof(TotalBeats));

        public static readonly StyledProperty<int> BeatsPerBarProperty =
            AvaloniaProperty.Register<TimelineGridControl, int>(nameof(BeatsPerBar), 4);

        // Bound to the timeline's scroll offset purely to trigger a repaint while scrolling; the actual
        // visible window is read live from the ancestor ScrollViewer in Render (so it's always accurate).
        public static readonly StyledProperty<double> HorizontalOffsetProperty =
            AvaloniaProperty.Register<TimelineGridControl, double>(nameof(HorizontalOffset));

        // Subtle contrast lines from the foreground token (light on dark themes, dark on light themes).
        private IPen _barPen = new Pen(Brushes.Gray, 1);
        private IPen _beatPen = new Pen(Brushes.Gray, 1);
        private IPen _subPen = new Pen(Brushes.Gray, 1);

        protected override void BuildThemeResources()
        {
            var fg = ThemePalette.Text;
            _barPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 80)), 1);
            _beatPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 40)), 1);
            _subPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(fg, 20)), 1);
        }

        static TimelineGridControl()
        {
            AffectsRender<TimelineGridControl>(PixelsPerBeatProperty, TotalBeatsProperty, BeatsPerBarProperty,
                HorizontalOffsetProperty);
        }

        public double PixelsPerBeat { get => GetValue(PixelsPerBeatProperty); set => SetValue(PixelsPerBeatProperty, value); }
        public double TotalBeats { get => GetValue(TotalBeatsProperty); set => SetValue(TotalBeatsProperty, value); }
        public int BeatsPerBar { get => GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }
        public double HorizontalOffset { get => GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }

        public override void Render(DrawingContext context)
        {
            var ppb = PixelsPerBeat;
            if (ppb <= 0) return;

            var bar = BeatsPerBar < 1 ? 1 : BeatsPerBar;
            var step = GridMath.SnapBeats(ppb, bar);
            if (step <= 0) return;
            var height = Bounds.Height;
            var totalBeats = TotalBeats;
            var lineCount = (int)Math.Ceiling(totalBeats / step);

            // Only draw the lines inside the visible viewport. The control's width spans the whole
            // arrangement (and at deep zoom that can be 100k+ px), so without this we'd issue thousands of
            // off-screen DrawLine calls per lane. Read the live scroll window from the ancestor ScrollViewer.
            var firstIndex = 0;
            var lastIndex = lineCount;
            if (this.FindAncestorOfType<ScrollViewer>() is { } sv && sv.Viewport.Width > 0)
            {
                var left = sv.Offset.X;
                var right = left + sv.Viewport.Width;
                firstIndex = Math.Max(0, (int)Math.Floor(left / (step * ppb)) - 1);
                lastIndex = Math.Min(lineCount, (int)Math.Ceiling(right / (step * ppb)) + 1);
            }

            for (var i = firstIndex; i <= lastIndex; i++)
            {
                var beat = i * step;
                var x = beat * ppb;

                // Classify the line: bar > beat > sub.
                var pen = _subPen;
                if (IsMultiple(beat, bar)) pen = _barPen;
                else if (IsMultiple(beat, 1.0)) pen = _beatPen;

                context.DrawLine(pen, new Point(x, 0), new Point(x, height));
            }
        }

        private static bool IsMultiple(double value, double of)
        {
            var ratio = value / of;
            return Math.Abs(ratio - Math.Round(ratio)) < 1e-6;
        }
    }
}
