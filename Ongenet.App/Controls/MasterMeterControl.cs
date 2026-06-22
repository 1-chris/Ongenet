using Avalonia;
using Avalonia.Media;
using Ongenet.App.Theming;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// Horizontal stereo master loudness meter for the top bar: two bars (L/R) with a green→red
    /// gradient on a dB scale, plus dB tick lines.
    /// </summary>
    public sealed class MasterMeterControl : ThemedControl
    {
        public static readonly StyledProperty<double> LevelLeftProperty =
            AvaloniaProperty.Register<MasterMeterControl, double>(nameof(LevelLeft));

        public static readonly StyledProperty<double> LevelRightProperty =
            AvaloniaProperty.Register<MasterMeterControl, double>(nameof(LevelRight));

        private IBrush _background = Brushes.Black;
        private IPen _tickPen = new Pen(Brushes.Gray, 1);

        protected override void BuildThemeResources()
        {
            _background = new SolidColorBrush(ThemePalette.Mantle);
            _tickPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Crust, 120)), 1);
        }

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

            context.FillRectangle(_background, new Rect(0, 0, w, h));

            const double gap = 2;
            var barH = (h - gap) / 2;
            DrawBar(context, new Rect(0, 0, w, barH), LevelLeft);
            DrawBar(context, new Rect(0, barH + gap, w, barH), LevelRight);

            // dB tick lines across the whole meter.
            foreach (var db in MeterScale.Ticks)
            {
                var x = MeterScale.NormalizeDb(db) * w;
                context.DrawLine(_tickPen, new Point(x, 0), new Point(x, h));
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
                    new GradientStop(ThemePalette.Green, 0.0),
                    new GradientStop(ThemePalette.Green, 0.6),
                    new GradientStop(ThemePalette.Yellow, 0.8),
                    new GradientStop(ThemePalette.Peach, 0.9),
                    new GradientStop(ThemePalette.Red, 1.0)
                }
            };

            context.FillRectangle(brush, new Rect(area.X, area.Y, area.Width * fill, area.Height));
        }
    }
}
