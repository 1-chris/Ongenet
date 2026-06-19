using System;
using Avalonia;
using Avalonia.Media;
using Ongenet.Desktop.Theming;

namespace Ongenet.Desktop.Controls
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
            AffectsRender<TimelineGridControl>(PixelsPerBeatProperty, TotalBeatsProperty, BeatsPerBarProperty);
        }

        public double PixelsPerBeat { get => GetValue(PixelsPerBeatProperty); set => SetValue(PixelsPerBeatProperty, value); }
        public double TotalBeats { get => GetValue(TotalBeatsProperty); set => SetValue(TotalBeatsProperty, value); }
        public int BeatsPerBar { get => GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }

        public override void Render(DrawingContext context)
        {
            var ppb = PixelsPerBeat;
            if (ppb <= 0) return;

            var bar = BeatsPerBar < 1 ? 1 : BeatsPerBar;
            var step = GridMath.SnapBeats(ppb, bar);
            var height = Bounds.Height;
            var totalBeats = TotalBeats;

            var lineCount = (int)Math.Ceiling(totalBeats / step);
            for (var i = 0; i <= lineCount; i++)
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
