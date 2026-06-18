using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// A thin vertical level meter (for track headers). <see cref="Level"/> is a linear peak
    /// (0..1+); it's drawn on a dB scale with a green→red gradient fixed to the control height.
    /// </summary>
    public sealed class LevelMeterControl : Control
    {
        public static readonly StyledProperty<double> LevelProperty =
            AvaloniaProperty.Register<LevelMeterControl, double>(nameof(Level));

        private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x25));

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

            context.FillRectangle(Background, new Rect(0, 0, w, h));

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
                    new GradientStop(Color.FromRgb(0xa6, 0xe3, 0xa1), 0.0),  // green
                    new GradientStop(Color.FromRgb(0xa6, 0xe3, 0xa1), 0.6),
                    new GradientStop(Color.FromRgb(0xf9, 0xe2, 0xaf), 0.8),  // yellow
                    new GradientStop(Color.FromRgb(0xfa, 0xb3, 0x87), 0.9),  // orange
                    new GradientStop(Color.FromRgb(0xf3, 0x38, 0x40), 1.0)   // bright red
                }
            };

            context.FillRectangle(brush, new Rect(0, h - fillH, w, fillH));
        }
    }
}
