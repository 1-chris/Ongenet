using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Horizontal stereo master loudness meter for the top bar: two bars (L/R) with a green→red
    /// gradient on a dB scale, plus dB tick lines.
    /// </summary>
    public sealed class MasterMeterControl : Control
    {
        public static readonly StyledProperty<double> LevelLeftProperty =
            AvaloniaProperty.Register<MasterMeterControl, double>(nameof(LevelLeft));

        public static readonly StyledProperty<double> LevelRightProperty =
            AvaloniaProperty.Register<MasterMeterControl, double>(nameof(LevelRight));

        private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x25));
        private static readonly IPen TickPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0x11, 0x11, 0x1b)), 1);

        static MasterMeterControl()
        {
            AffectsRender<MasterMeterControl>(LevelLeftProperty, LevelRightProperty);
        }

        public double LevelLeft { get => GetValue(LevelLeftProperty); set => SetValue(LevelLeftProperty, value); }
        public double LevelRight { get => GetValue(LevelRightProperty); set => SetValue(LevelRightProperty, value); }

        public override void Render(DrawingContext context)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 4 || h < 4) return;

            context.FillRectangle(Background, new Rect(0, 0, w, h));

            const double gap = 2;
            var barH = (h - gap) / 2;
            DrawBar(context, new Rect(0, 0, w, barH), LevelLeft);
            DrawBar(context, new Rect(0, barH + gap, w, barH), LevelRight);

            // dB tick lines across the whole meter.
            foreach (var db in MeterScale.Ticks)
            {
                var x = MeterScale.NormalizeDb(db) * w;
                context.DrawLine(TickPen, new Point(x, 0), new Point(x, h));
            }
        }

        private static void DrawBar(DrawingContext context, Rect area, double level)
        {
            var fill = MeterScale.Normalize(level);
            if (fill <= 0) return;

            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(area.Width, 0, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xa6, 0xe3, 0xa1), 0.0),  // green
                    new GradientStop(Color.FromRgb(0xa6, 0xe3, 0xa1), 0.6),
                    new GradientStop(Color.FromRgb(0xf9, 0xe2, 0xaf), 0.8),  // yellow
                    new GradientStop(Color.FromRgb(0xfa, 0xb3, 0x87), 0.9),  // orange
                    new GradientStop(Color.FromRgb(0xf3, 0x38, 0x40), 1.0)   // bright red
                }
            };

            context.FillRectangle(brush, new Rect(area.X, area.Y, area.Width * fill, area.Height));
        }
    }
}
