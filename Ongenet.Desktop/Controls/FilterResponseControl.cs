using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Frequency-response analyser for the <see cref="FilterEffect"/>: on top of the shared
    /// spectrum/grid base, it plots the filter's magnitude curve and a draggable marker at the
    /// cutoff. Dragging maps X→<see cref="Frequency"/> (log) and Y→<see cref="Resonance"/>, both
    /// two-way bound to the knobs.
    /// </summary>
    public sealed class FilterResponseControl : SpectrumGraphControl
    {
        private const double MinQ = 0.5;
        private const double MaxQ = 16.0;
        private const double TopDb = 18.0;
        private const double BotDb = -36.0;

        public static readonly StyledProperty<double> FrequencyProperty =
            AvaloniaProperty.Register<FilterResponseControl, double>(nameof(Frequency), 1000.0,
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<double> ResonanceProperty =
            AvaloniaProperty.Register<FilterResponseControl, double>(nameof(Resonance), 0.7,
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<int> ModeProperty =
            AvaloniaProperty.Register<FilterResponseControl, int>(nameof(Mode));

        public static readonly StyledProperty<double> SampleRateProperty =
            AvaloniaProperty.Register<FilterResponseControl, double>(nameof(SampleRate), 44100.0);

        private static readonly IPen ZeroPen = new Pen(new SolidColorBrush(Color.FromRgb(0x58, 0x5b, 0x70)), 1); // overlay0
        private static readonly IPen CurvePen = new Pen(new SolidColorBrush(Color.FromRgb(0xcb, 0xa6, 0xf7)), 2) { LineJoin = PenLineJoin.Round }; // mauve
        private static readonly IBrush FillBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xcb, 0xa6, 0xf7));
        private static readonly IBrush MarkerBrush = new SolidColorBrush(Color.FromRgb(0xcb, 0xa6, 0xf7));
        private static readonly IPen MarkerRing = new Pen(new SolidColorBrush(Color.FromRgb(0xcd, 0xd6, 0xf4)), 2); // text

        private bool _dragging;

        static FilterResponseControl()
        {
            AffectsRender<FilterResponseControl>(FrequencyProperty, ResonanceProperty, ModeProperty, SampleRateProperty);
        }

        public FilterResponseControl() => Cursor = new Cursor(StandardCursorType.Hand);

        public double Frequency { get => GetValue(FrequencyProperty); set => SetValue(FrequencyProperty, value); }
        public double Resonance { get => GetValue(ResonanceProperty); set => SetValue(ResonanceProperty, value); }
        public int Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
        public double SampleRate { get => GetValue(SampleRateProperty); set => SetValue(SampleRateProperty, value); }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true;
            e.Pointer.Capture(this);
            UpdateFromPointer(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging) return;
            UpdateFromPointer(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void UpdateFromPointer(Point p)
        {
            var w = Bounds.Width;
            var plotH = Bounds.Height - LabelStripHeight;
            if (w < 1 || plotH < 1) return;
            Frequency = Math.Clamp(XToFreq(Math.Clamp(p.X, 0, w), w), MinFreq, MaxFreq);
            var t = 1.0 - Math.Clamp(p.Y / plotH, 0, 1); // top of plot = max resonance
            Resonance = MinQ + t * (MaxQ - MinQ);
        }

        protected override void RenderOverlay(DrawingContext context, double w, double plotH)
        {
            var zeroY = DbToY(0, plotH);
            context.DrawLine(ZeroPen, new Point(0, zeroY), new Point(w, zeroY));

            var sr = SampleRate > 0 ? SampleRate : 44100.0;
            var coeffs = BiquadCoefficients.Compute((FilterMode)Mode, Frequency, Resonance, sr);

            var stroke = new StreamGeometry();
            var fill = new StreamGeometry();
            using (var sctx = stroke.Open())
            using (var fctx = fill.Open())
            {
                var first = true;
                fctx.BeginFigure(new Point(0, plotH), true);
                for (var px = 0.0; px <= w; px += 1)
                {
                    var db = coeffs.MagnitudeDb(XToFreq(px, w), sr);
                    var pt = new Point(px, DbToY(db, plotH));
                    if (first) { sctx.BeginFigure(pt, false); first = false; }
                    else sctx.LineTo(pt);
                    fctx.LineTo(pt);
                }

                sctx.EndFigure(false);
                fctx.LineTo(new Point(w, plotH));
                fctx.EndFigure(true);
            }

            context.DrawGeometry(FillBrush, null, fill);
            context.DrawGeometry(null, CurvePen, stroke);

            if ((FilterMode)Mode != FilterMode.Bypass)
            {
                var mx = FreqToX(Frequency, w);
                var my = DbToY(coeffs.MagnitudeDb(Frequency, sr), plotH);
                context.DrawEllipse(MarkerBrush, MarkerRing,
                    new Point(Math.Clamp(mx, 0, w), Math.Clamp(my, 0, plotH)), 5, 5);
            }
        }

        private static double DbToY(double db, double plotH)
            => Math.Clamp((TopDb - db) / (TopDb - BotDb), 0, 1) * plotH;
    }
}
