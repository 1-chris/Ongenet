using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Renders the piano-roll's top ruler: a bar number + tick at each bar line.
    /// </summary>
    public sealed class PianoRollRulerControl : Control
    {
        public static readonly StyledProperty<double> PixelsPerBeatProperty =
            AvaloniaProperty.Register<PianoRollRulerControl, double>(nameof(PixelsPerBeat));

        public static readonly StyledProperty<double> TotalBeatsProperty =
            AvaloniaProperty.Register<PianoRollRulerControl, double>(nameof(TotalBeats));

        public static readonly StyledProperty<int> BeatsPerBarProperty =
            AvaloniaProperty.Register<PianoRollRulerControl, int>(nameof(BeatsPerBar), 4);

        private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromArgb(180, 205, 214, 244));
        private static readonly IPen BarPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1);

        static PianoRollRulerControl()
        {
            AffectsRender<PianoRollRulerControl>(PixelsPerBeatProperty, TotalBeatsProperty, BeatsPerBarProperty);
        }

        public double PixelsPerBeat { get => GetValue(PixelsPerBeatProperty); set => SetValue(PixelsPerBeatProperty, value); }
        public double TotalBeats { get => GetValue(TotalBeatsProperty); set => SetValue(TotalBeatsProperty, value); }
        public int BeatsPerBar { get => GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }

        public override void Render(DrawingContext context)
        {
            var ppb = PixelsPerBeat;
            if (ppb <= 0) return;

            var bar = BeatsPerBar < 1 ? 4 : BeatsPerBar;
            var height = Bounds.Height;
            var totalBeats = (int)System.Math.Ceiling(TotalBeats);

            for (var beat = 0; beat <= totalBeats; beat += bar)
            {
                var x = beat * ppb;
                context.DrawLine(BarPen, new Point(x, 0), new Point(x, height));

                var label = new FormattedText(
                    (beat / bar + 1).ToString(),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 11, LabelBrush);
                context.DrawText(label, new Point(x + 3, 3));
            }
        }
    }
}
