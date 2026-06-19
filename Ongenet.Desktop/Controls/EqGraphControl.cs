using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ongenet.Core.Audio.Effects;
using Ongenet.Desktop.Theming;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// Interactive parametric-EQ graph built on the shared spectrum/grid base. Draws the combined
    /// band response over the live spectrum and a draggable node per band. Double-click adds a bell
    /// band; drag moves frequency (X) and gain (Y); the mouse wheel over a node sets its Q;
    /// right-click opens a menu to change the band type or delete it.
    /// </summary>
    public sealed class EqGraphControl : SpectrumGraphControl
    {
        private const double TopDb = 18.0;
        private const double BotDb = -18.0;
        private const double HitRadius = 10.0;

        public static readonly StyledProperty<EqEffect?> EqProperty =
            AvaloniaProperty.Register<EqGraphControl, EqEffect?>(nameof(Eq));

        private IPen _zeroPen = new Pen(Brushes.Gray, 1);
        private IPen _curvePen = new Pen(Brushes.Gray, 2);
        private IBrush _fillBrush = Brushes.Gray;
        private IBrush _nodeBrush = Brushes.Gray;
        private IBrush _nodeSelBrush = Brushes.Gray;
        private IPen _nodeRing = new Pen(Brushes.Gray, 2);

        protected override void BuildThemeResources()
        {
            base.BuildThemeResources();
            _zeroPen = new Pen(new SolidColorBrush(ThemePalette.Overlay0), 1);
            _curvePen = new Pen(new SolidColorBrush(ThemePalette.Mauve), 2) { LineJoin = PenLineJoin.Round };
            _fillBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Mauve, 0x33));
            _nodeBrush = new SolidColorBrush(ThemePalette.Blue);
            _nodeSelBrush = new SolidColorBrush(ThemePalette.Mauve);
            _nodeRing = new Pen(new SolidColorBrush(ThemePalette.Text), 2);
        }

        private EqBand? _dragBand;
        private EqBand? _selected;

        static EqGraphControl()
        {
            AffectsRender<EqGraphControl>(EqProperty);
        }

        public EqGraphControl() => Cursor = new Cursor(StandardCursorType.Hand);

        public EqEffect? Eq { get => GetValue(EqProperty); set => SetValue(EqProperty, value); }

        private double SampleRate => Eq?.SampleRate > 0 ? Eq.SampleRate : 44100.0;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (Eq is not { } eq) return;
            var p = e.GetPosition(this);
            var props = e.GetCurrentPoint(this).Properties;
            var hit = HitTest(p);

            if (props.IsRightButtonPressed)
            {
                if (hit is not null) ShowBandMenu(eq, hit);
                e.Handled = true;
                return;
            }

            if (!props.IsLeftButtonPressed) return;

            if (hit is null)
            {
                if (e.ClickCount == 2)
                {
                    // Add a bell band at the clicked position.
                    var band = new EqBand(EqBandType.Bell, XToFreq(Clamp(p.X, Bounds.Width), Bounds.Width),
                        YToDb(p.Y), 1.0);
                    eq.AddBand(band);
                    _selected = band;
                    _dragBand = band;
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                    e.Handled = true;
                }

                return;
            }

            _selected = hit;
            _dragBand = hit;
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_dragBand is null) return;
            var p = e.GetPosition(this);
            _dragBand.Frequency = Math.Clamp(XToFreq(Clamp(p.X, Bounds.Width), Bounds.Width), MinFreq, MaxFreq);
            if (HasGain(_dragBand.Type)) _dragBand.GainDb = YToDb(p.Y);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_dragBand is null) return;
            _dragBand = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            var hit = HitTest(e.GetPosition(this));
            if (hit is null) return;
            hit.Q = Math.Clamp(hit.Q * Math.Exp(e.Delta.Y * 0.15), 0.1, 16.0);
            InvalidateVisual();
            e.Handled = true;
        }

        private void ShowBandMenu(EqEffect eq, EqBand band)
        {
            var flyout = new MenuFlyout();

            void AddType(string name, EqBandType type)
            {
                var item = new MenuItem { Header = name };
                item.Click += (_, _) => { band.Type = type; InvalidateVisual(); };
                flyout.Items.Add(item);
            }

            AddType("Bell", EqBandType.Bell);
            AddType("Low Shelf", EqBandType.LowShelf);
            AddType("High Shelf", EqBandType.HighShelf);
            AddType("High-pass", EqBandType.HighPass);
            AddType("Low-pass", EqBandType.LowPass);
            AddType("Notch", EqBandType.Notch);
            flyout.Items.Add(new Separator());
            var del = new MenuItem { Header = "Delete band" };
            del.Click += (_, _) =>
            {
                eq.RemoveBand(band);
                if (ReferenceEquals(_selected, band)) _selected = null;
                InvalidateVisual();
            };
            flyout.Items.Add(del);

            flyout.ShowAt(this, true);
        }

        protected override void RenderOverlay(DrawingContext context, double w, double plotH)
        {
            var zeroY = DbToY(0, plotH);
            context.DrawLine(_zeroPen, new Point(0, zeroY), new Point(w, zeroY));

            if (Eq is not { } eq) return;
            var bands = eq.Bands;
            var sr = SampleRate;

            // Combined response = sum of each band's dB.
            var coeffs = new BiquadCoefficients[bands.Count];
            for (var i = 0; i < bands.Count; i++)
                coeffs[i] = BiquadCoefficients.ComputeEq(bands[i].Type, bands[i].Frequency, bands[i].Q, bands[i].GainDb, sr);

            var stroke = new StreamGeometry();
            var fill = new StreamGeometry();
            using (var sctx = stroke.Open())
            using (var fctx = fill.Open())
            {
                var first = true;
                fctx.BeginFigure(new Point(0, zeroY), false);
                for (var px = 0.0; px <= w; px += 1)
                {
                    var freq = XToFreq(px, w);
                    double db = 0;
                    foreach (var c in coeffs) db += c.MagnitudeDb(freq, sr);
                    var pt = new Point(px, DbToY(db, plotH));
                    if (first) { sctx.BeginFigure(pt, false); first = false; }
                    else sctx.LineTo(pt);
                    fctx.LineTo(pt);
                }

                sctx.EndFigure(false);
                fctx.LineTo(new Point(w, zeroY));
                fctx.EndFigure(true);
            }

            context.DrawGeometry(_fillBrush, null, fill);
            context.DrawGeometry(null, _curvePen, stroke);

            // Band nodes.
            foreach (var band in bands)
            {
                var c = NodePoint(band, w, plotH);
                var sel = ReferenceEquals(band, _selected);
                context.DrawEllipse(sel ? _nodeSelBrush : _nodeBrush, _nodeRing, c, sel ? 7 : 5, sel ? 7 : 5);
            }
        }

        private Point NodePoint(EqBand band, double w, double plotH)
        {
            var x = FreqToX(band.Frequency, w);
            var y = HasGain(band.Type) ? DbToY(band.GainDb, plotH) : DbToY(0, plotH);
            return new Point(Math.Clamp(x, 0, w), Math.Clamp(y, 0, plotH));
        }

        private EqBand? HitTest(Point p)
        {
            if (Eq is not { } eq) return null;
            var w = Bounds.Width;
            var plotH = Bounds.Height - LabelStripHeight;
            EqBand? best = null;
            var bestDist = HitRadius;
            foreach (var band in eq.Bands)
            {
                var c = NodePoint(band, w, plotH);
                var d = Math.Sqrt((c.X - p.X) * (c.X - p.X) + (c.Y - p.Y) * (c.Y - p.Y));
                if (d <= bestDist) { bestDist = d; best = band; }
            }

            return best;
        }

        private static bool HasGain(EqBandType type)
            => type is EqBandType.Bell or EqBandType.LowShelf or EqBandType.HighShelf;

        private static double Clamp(double v, double max) => Math.Clamp(v, 0, max);

        private static double DbToY(double db, double plotH)
            => Math.Clamp((TopDb - db) / (TopDb - BotDb), 0, 1) * plotH;

        private double YToDb(double y)
        {
            var plotH = Bounds.Height - LabelStripHeight;
            var t = plotH > 0 ? Math.Clamp(y / plotH, 0, 1) : 0;
            return Math.Clamp(TopDb - t * (TopDb - BotDb), BotDb, TopDb);
        }
    }
}
