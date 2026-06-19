using Avalonia;
using Avalonia.Media;
using Ongenet.Desktop.Theming;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// A thin vertical level meter (for track headers). <see cref="Level"/> is a linear peak
    /// (0..1+); it's drawn on a dB scale with a green→red gradient fixed to the control height.
    /// </summary>
    public sealed class LevelMeterControl : ThemedControl
    {
        public static readonly StyledProperty<double> LevelProperty =
            AvaloniaProperty.Register<LevelMeterControl, double>(nameof(Level));

        private IBrush _background = Brushes.Black;

        protected override void BuildThemeResources() => _background = new SolidColorBrush(ThemePalette.Mantle);

        static LevelMeterControl()
        {
            AffectsRender<LevelMeterControl>(LevelProperty);
        }

        public double Level
        {
            get => GetValue(LevelProperty);
            set => SetValue(LevelProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 1 || h < 1) return;

            context.FillRectangle(_background, new Rect(0, 0, w, h));

            var fill = MeterScale.Normalize(Level);
            if (fill <= 0) return;

            var fillH = h * fill;
            // Gradient fixed to the full control height (green bottom → red top), so the fill
            // shows the correct colour band regardless of its height.
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, h, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(ThemePalette.Green, 0.0),
                    new GradientStop(ThemePalette.Green, 0.6),
                    new GradientStop(ThemePalette.Yellow, 0.8),
                    new GradientStop(ThemePalette.Peach, 0.9),
                    new GradientStop(ThemePalette.Red, 1.0)
                }
            };

            context.FillRectangle(brush, new Rect(0, h - fillH, w, fillH));
        }
    }
}
