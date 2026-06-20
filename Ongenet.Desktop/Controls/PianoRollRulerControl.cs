using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Ongenet.Desktop.Theming;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Renders the piano-roll's top ruler: a bar number + tick at each bar line.
    /// </summary>
    public sealed class PianoRollRulerControl : ThemedControl
    {
        public static readonly StyledProperty<double> PixelsPerBeatProperty =
            AvaloniaProperty.Register<PianoRollRulerControl, double>(nameof(PixelsPerBeat));

        public static readonly StyledProperty<double> TotalBeatsProperty =
            AvaloniaProperty.Register<PianoRollRulerControl, double>(nameof(TotalBeats));

        public static readonly StyledProperty<int> BeatsPerBarProperty =
            AvaloniaProperty.Register<PianoRollRulerControl, int>(nameof(BeatsPerBar), 4);

        private IBrush _labelBrush = Brushes.Gray;        // subtext
        private IPen _barPen = new Pen(Brushes.Gray, 1);  // text (faint)

        // Explicit text typeface. Typeface.Default resolves to the app default family whose glyph
        // fallback can bind digits to the emoji font on Win/Mac; naming the text chain avoids that.
        private static readonly Typeface LabelTypeface = new(new FontFamily("Inter, Noto Sans, sans-serif"));

        protected override void BuildThemeResources()
        {
            _labelBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Text, 180));
            _barPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Text, 90)), 1);
        }

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
                context.DrawLine(_barPen, new Point(x, 0), new Point(x, height));

                var label = new FormattedText(
                    (beat / bar + 1).ToString(),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    LabelTypeface, 11, _labelBrush);
                context.DrawText(label, new Point(x + 3, 3));
            }
        }
    }
}
