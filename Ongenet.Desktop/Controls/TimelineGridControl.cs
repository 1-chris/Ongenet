using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Draws the timeline's vertical grid behind a lane's clips: strong bar lines, medium beat
    /// lines, and faint sub-beat lines, with the sub-beat density following the zoom.
    /// </summary>
    public sealed class TimelineGridControl : Control
    {
        public static readonly StyledProperty<double> PixelsPerBeatProperty =
            AvaloniaProperty.Register<TimelineGridControl, double>(nameof(PixelsPerBeat));

        public static readonly StyledProperty<double> TotalBeatsProperty =
            AvaloniaProperty.Register<TimelineGridControl, double>(nameof(TotalBeats));

        public static readonly StyledProperty<int> BeatsPerBarProperty =
            AvaloniaProperty.Register<TimelineGridControl, int>(nameof(BeatsPerBar), 4);

        private static readonly IPen BarPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);
        private static readonly IPen BeatPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
        private static readonly IPen SubPen = new Pen(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), 1);

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
                var pen = SubPen;
                if (IsMultiple(beat, bar)) pen = BarPen;
                else if (IsMultiple(beat, 1.0)) pen = BeatPen;

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
