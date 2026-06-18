using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// A compact rotary knob for numeric parameters. Drag vertically (or scroll) to change the
    /// value; a 270° arc shows the value and a pointer line indicates the position. More
    /// space-efficient than a slider for synth/effect controls.
    /// </summary>
    public sealed class Knob : Control
    {
        // Sweep: 225° (down-left) clockwise-by-screen to -45° (down-right), 270° total, gap at bottom.
        private const double StartDeg = 225.0;
        private const double SweepDeg = 270.0;
        private const double DragRangePixels = 150.0; // vertical pixels for the full range

        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<Knob, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<double> MinimumProperty =
            AvaloniaProperty.Register<Knob, double>(nameof(Minimum));

        public static readonly StyledProperty<double> MaximumProperty =
            AvaloniaProperty.Register<Knob, double>(nameof(Maximum), 1.0);

        // Curve exponent: value = min + (max-min)·t^Skew. 1 = linear; >1 = finer near the minimum.
        public static readonly StyledProperty<double> SkewProperty =
            AvaloniaProperty.Register<Knob, double>(nameof(Skew), 1.0);

        private static readonly IBrush BodyBrush = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44));   // surface0
        private static readonly IPen TrackPen = new Pen(new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5a)), 3) { LineCap = PenLineCap.Round };
        private static readonly IPen ValuePen = new Pen(new SolidColorBrush(Color.FromRgb(0xcb, 0xa6, 0xf7)), 3) { LineCap = PenLineCap.Round }; // mauve
        private static readonly IPen IndicatorPen = new Pen(new SolidColorBrush(Color.FromRgb(0xcd, 0xd6, 0xf4)), 2) { LineCap = PenLineCap.Round }; // text

        private bool _dragging;
        private double _dragStartY;
        private double _dragStartT;

        static Knob()
        {
            AffectsRender<Knob>(ValueProperty, MinimumProperty, MaximumProperty, SkewProperty);
        }

        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public double Skew { get => GetValue(SkewProperty); set => SetValue(SkewProperty, value); }

        // The knob position in [0,1] for the current value (inverse of the skew curve).
        private double Normalized
        {
            get
            {
                var range = Maximum - Minimum;
                if (range <= 0) return 0;
                var lin = (Value - Minimum) / range;
                lin = lin < 0 ? 0 : lin > 1 ? 1 : lin;
                var skew = Skew;
                return skew == 1.0 ? lin : Math.Pow(lin, 1.0 / skew);
            }
        }

        // Maps a knob position in [0,1] back to a value through the skew curve.
        private double ValueFromT(double t)
        {
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            var skew = Skew;
            var lin = skew == 1.0 ? t : Math.Pow(t, skew);
            return Minimum + lin * (Maximum - Minimum);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            // Right-click: offer "Create automation track" for the bound float parameter.
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed
                && DataContext is ViewModels.FloatParameterViewModel fp)
            {
                AutomationGesture.Offer(this, AutomationGesture.ForFloat(fp.Parameter));
                e.Handled = true;
                return;
            }

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true;
            _dragStartY = e.GetPosition(this).Y;
            _dragStartT = Normalized;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging) return;
            var dy = _dragStartY - e.GetPosition(this).Y; // up = increase
            Value = ValueFromT(_dragStartT + dy / DragRangePixels);
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

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            Value = ValueFromT(Normalized + e.Delta.Y / 20.0);
            e.Handled = true;
        }

        public override void Render(DrawingContext context)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 4 || h < 4) return;

            var center = new Point(w / 2, h / 2);
            var radius = Math.Min(w, h) / 2 - 3;
            var t = Normalized;

            // Knob body.
            context.DrawEllipse(BodyBrush, null, center, radius * 0.62, radius * 0.62);

            // Track arc (full sweep) + value arc (start → current).
            DrawArc(context, TrackPen, center, radius, StartDeg, StartDeg - SweepDeg);
            DrawArc(context, ValuePen, center, radius, StartDeg, StartDeg - SweepDeg * t);

            // Indicator line.
            var tip = PointOnCircle(center, radius * 0.82, StartDeg - SweepDeg * t);
            var basePt = PointOnCircle(center, radius * 0.28, StartDeg - SweepDeg * t);
            context.DrawLine(IndicatorPen, basePt, tip);
        }

        // Draws an arc by sampling points along it (avoids ArcTo sweep-direction pitfalls).
        private static void DrawArc(DrawingContext context, IPen pen, Point center, double radius, double fromDeg, double toDeg)
        {
            const int segments = 36;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(PointOnCircle(center, radius, fromDeg), false);
                for (var i = 1; i <= segments; i++)
                {
                    var deg = fromDeg + (toDeg - fromDeg) * i / segments;
                    ctx.LineTo(PointOnCircle(center, radius, deg));
                }

                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }

        private static Point PointOnCircle(Point center, double radius, double degrees)
        {
            var rad = degrees * Math.PI / 180.0;
            return new Point(center.X + radius * Math.Cos(rad), center.Y - radius * Math.Sin(rad));
        }
    }
}
