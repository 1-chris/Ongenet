using System;
using Avalonia;
using Avalonia.Media;
using Ongenet.Desktop.Theming;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Draws a DAHDSR envelope as a curve: delay (flat 0) → attack (rise) → hold → decay → a fixed
    /// sustain plateau → release. Times are in seconds; sustain is a level 0–1. Used to show the SFZ
    /// amp envelope of the loaded patch.
    /// </summary>
    public sealed class EnvelopeDisplayControl : ThemedControl
    {
        public static readonly StyledProperty<double> DelayProperty =
            AvaloniaProperty.Register<EnvelopeDisplayControl, double>(nameof(Delay));
        public static readonly StyledProperty<double> AttackProperty =
            AvaloniaProperty.Register<EnvelopeDisplayControl, double>(nameof(Attack));
        public static readonly StyledProperty<double> HoldProperty =
            AvaloniaProperty.Register<EnvelopeDisplayControl, double>(nameof(Hold));
        public static readonly StyledProperty<double> DecayProperty =
            AvaloniaProperty.Register<EnvelopeDisplayControl, double>(nameof(Decay));
        public static readonly StyledProperty<double> SustainProperty =
            AvaloniaProperty.Register<EnvelopeDisplayControl, double>(nameof(Sustain), 1.0);
        public static readonly StyledProperty<double> ReleaseProperty =
            AvaloniaProperty.Register<EnvelopeDisplayControl, double>(nameof(Release));

        private IPen _curvePen = new Pen(Brushes.Gray, 2);
        private IBrush _fill = Brushes.Gray;
        private IPen _axisPen = new Pen(Brushes.Gray, 1);

        static EnvelopeDisplayControl()
        {
            AffectsRender<EnvelopeDisplayControl>(DelayProperty, AttackProperty, HoldProperty,
                DecayProperty, SustainProperty, ReleaseProperty);
        }

        public double Delay { get => GetValue(DelayProperty); set => SetValue(DelayProperty, value); }
        public double Attack { get => GetValue(AttackProperty); set => SetValue(AttackProperty, value); }
        public double Hold { get => GetValue(HoldProperty); set => SetValue(HoldProperty, value); }
        public double Decay { get => GetValue(DecayProperty); set => SetValue(DecayProperty, value); }
        public double Sustain { get => GetValue(SustainProperty); set => SetValue(SustainProperty, value); }
        public double Release { get => GetValue(ReleaseProperty); set => SetValue(ReleaseProperty, value); }

        protected override void BuildThemeResources()
        {
            _curvePen = new Pen(new SolidColorBrush(ThemePalette.Green), 2) { LineJoin = PenLineJoin.Round };
            _fill = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Green, 0x33));
            _axisPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Overlay0, 0x80)), 1);
        }

        public override void Render(DrawingContext context)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 4 || h < 4) return;

            const double pad = 4;
            var top = pad;
            var bottom = h - pad;
            var left = pad;
            var right = w - pad;
            var usableW = right - left;
            var usableH = bottom - top;

            context.DrawLine(_axisPen, new Point(left, bottom), new Point(right, bottom));

            // Allocate horizontal space proportionally to stage times, with a fixed sustain plateau so
            // the shape stays readable even when all times are tiny.
            var delay = Math.Max(0, Delay);
            var attack = Math.Max(0, Attack);
            var hold = Math.Max(0, Hold);
            var decay = Math.Max(0, Decay);
            var release = Math.Max(0, Release);
            var variable = delay + attack + hold + decay + release;
            var sustainSpan = variable <= 0 ? usableW * 0.25 : usableW * 0.18;
            var scale = variable > 0 ? (usableW - sustainSpan) / variable : 0;

            var sustain = Math.Clamp(Sustain, 0, 1);
            double Y(double level) => bottom - level * usableH;

            var x = left;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(x, bottom), isFilled: true); // baseline start
                x += delay * scale; g.LineTo(new Point(x, Y(0)));     // delay (flat 0)
                x += attack * scale; g.LineTo(new Point(x, Y(1)));    // attack -> peak
                x += hold * scale; g.LineTo(new Point(x, Y(1)));      // hold
                x += decay * scale; g.LineTo(new Point(x, Y(sustain))); // decay -> sustain
                x += sustainSpan; g.LineTo(new Point(x, Y(sustain)));   // sustain plateau
                x += release * scale; g.LineTo(new Point(x, Y(0)));     // release -> 0
                g.LineTo(new Point(x, bottom));
                g.EndFigure(true);
            }

            context.DrawGeometry(_fill, null, geo);

            // Stroke the contour (without the closing baseline) on top.
            var line = new StreamGeometry();
            x = left;
            using (var g = line.Open())
            {
                g.BeginFigure(new Point(x, Y(0)), isFilled: false);
                x += delay * scale; g.LineTo(new Point(x, Y(0)));
                x += attack * scale; g.LineTo(new Point(x, Y(1)));
                x += hold * scale; g.LineTo(new Point(x, Y(1)));
                x += decay * scale; g.LineTo(new Point(x, Y(sustain)));
                x += sustainSpan; g.LineTo(new Point(x, Y(sustain)));
                x += release * scale; g.LineTo(new Point(x, Y(0)));
                g.EndFigure(false);
            }

            context.DrawGeometry(null, _curvePen, line);
        }
    }
}
