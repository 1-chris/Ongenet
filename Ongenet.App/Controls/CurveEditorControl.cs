using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Modulation;
using Ongenet.App.Services;
using Ongenet.App.Theming;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// A reusable, self-contained editor for a <see cref="ModulationCurve"/> — the breakpoint graph used
    /// by the stutter effect's gestures, but deliberately generic so any future effect or plugin can host
    /// one. The X axis is normalised phase (0..1), the Y axis is normalised value (0..1). Interaction
    /// mirrors the timeline's automation editor: double-click adds a point (snapped to the subdivision
    /// grid), dragging a handle moves it, dragging a segment bends its tension, and right-click deletes.
    /// Evaluation/segment shaping is shared with <see cref="AutomationLane.Shape"/> so the drawn line
    /// matches playback. Edits mutate the bound curve in place (the same instance the engine reads).
    /// </summary>
    public sealed class CurveEditorControl : ThemedControl
    {
        public static readonly StyledProperty<ModulationCurve?> CurveProperty =
            AvaloniaProperty.Register<CurveEditorControl, ModulationCurve?>(nameof(Curve));

        /// <summary>Number of equal phase subdivisions to snap to and draw as gridlines (0 = no snap).</summary>
        public static readonly StyledProperty<int> SnapDivisionsProperty =
            AvaloniaProperty.Register<CurveEditorControl, int>(nameof(SnapDivisions), 16);

        /// <summary>Bump to force a repaint when the bound curve mutates in place.</summary>
        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<CurveEditorControl, int>(nameof(Revision));

        private const double Pad = 8.0;
        private const double HandleRadius = 4.0;
        private const double HitRadius = 8.0;
        private const double OnLineSlack = 6.0;
        private const double BendThreshold = 3.0;
        private const double DoubleClickMs = 400.0;
        private const double DoubleClickSlop = 8.0;

        private IPen _linePen = new Pen(Brushes.Gray, 1.6);
        private IPen _gridPen = new Pen(Brushes.DimGray, 1);
        private IBrush _handleFill = Brushes.Gray;
        private IPen _handleStroke = new Pen(Brushes.Black, 1);
        private IBrush _fill = new SolidColorBrush(Colors.Gray, 0.12);

        private enum Drag { None, Move, Bend }

        private static IHistoryService? History => App.ServiceProvider?.GetService<IHistoryService>();

        private Drag _drag = Drag.None;
        private AutomationPoint? _dragPoint;
        private int _bendIndex = -1;
        private double _bendStartCurve;
        private double _bendStartY;
        private long _lastPressTick;
        private Point _lastPressPos;

        static CurveEditorControl()
        {
            AffectsRender<CurveEditorControl>(CurveProperty, RevisionProperty, SnapDivisionsProperty);
        }

        public ModulationCurve? Curve
        {
            get => GetValue(CurveProperty);
            set => SetValue(CurveProperty, value);
        }

        public int SnapDivisions
        {
            get => GetValue(SnapDivisionsProperty);
            set => SetValue(SnapDivisionsProperty, value);
        }

        public int Revision
        {
            get => GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
        }

        protected override void BuildThemeResources()
        {
            _linePen = new Pen(new SolidColorBrush(ThemePalette.Mauve), 1.8);
            _gridPen = new Pen(new SolidColorBrush(ThemePalette.Surface1), 1);
            _handleFill = new SolidColorBrush(ThemePalette.Text);
            _handleStroke = new Pen(new SolidColorBrush(ThemePalette.Base), 1);
            _fill = new SolidColorBrush(ThemePalette.Mauve, 0.12);
        }

        public override void Render(DrawingContext context)
        {
            var curve = Curve;
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 1 || h < 1) return;

            // Subdivision gridlines + a centre line.
            var div = SnapDivisions;
            if (div > 0)
                for (var i = 1; i < div; i++)
                {
                    var x = i / (double)div * w;
                    context.DrawLine(_gridPen, new Point(x, 0), new Point(x, h));
                }

            context.DrawLine(_gridPen, new Point(0, h / 2), new Point(w, h / 2));

            if (curve is null || curve.Points.Count == 0) return;
            var pts = curve.Points;

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                var first = pts[0];
                var startY = ValueToY(first.Value, h);
                gc.BeginFigure(new Point(0, startY), false);
                gc.LineTo(new Point(PhaseToX(first.Beat, w), startY));

                for (var i = 0; i < pts.Count - 1; i++)
                {
                    var p0 = pts[i];
                    var p1 = pts[i + 1];
                    var x0 = PhaseToX(p0.Beat, w);
                    var x1 = PhaseToX(p1.Beat, w);
                    var steps = p0.Curve == 0 ? 1 : (int)Math.Clamp(Math.Abs(x1 - x0) / 6.0, 2, 48);
                    for (var s = 1; s <= steps; s++)
                    {
                        var f = (double)s / steps;
                        var val = p0.Value + (p1.Value - p0.Value) * AutomationLane.Shape(f, p0.Curve);
                        gc.LineTo(new Point(x0 + (x1 - x0) * f, ValueToY(val, h)));
                    }
                }

                var last = pts[pts.Count - 1];
                var lastY = ValueToY(last.Value, h);
                gc.LineTo(new Point(PhaseToX(last.Beat, w), lastY));
                gc.LineTo(new Point(w, lastY));
            }

            context.DrawGeometry(null, _linePen, geo);

            foreach (var p in pts)
            {
                var c = new Point(PhaseToX(p.Beat, w), ValueToY(p.Value, h));
                context.DrawEllipse(_handleFill, _handleStroke, c, HandleRadius, HandleRadius);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var curve = Curve;
            if (curve is null) { base.OnPointerPressed(e); return; }
            var w = Bounds.Width;
            var h = Bounds.Height;
            var pos = e.GetPosition(this);
            var props = e.GetCurrentPoint(this).Properties;

            if (props.IsRightButtonPressed)
            {
                var victim = HitPoint(pos, curve, w, h);
                if (victim is not null && curve.Points.Count > 1)
                {
                    History?.Capture("Delete curve point");
                    curve.Points.Remove(victim);
                    InvalidateVisual();
                    e.Handled = true;
                }

                return;
            }

            if (!props.IsLeftButtonPressed) { base.OnPointerPressed(e); return; }

            e.Handled = true;
            e.Pointer.Capture(this);

            var now = Environment.TickCount64;
            var isDouble = now - _lastPressTick <= DoubleClickMs && Distance(pos, _lastPressPos) <= DoubleClickSlop;
            _lastPressTick = isDouble ? 0 : now;
            _lastPressPos = pos;

            var hit = HitPoint(pos, curve, w, h);
            if (hit is not null)
            {
                History?.Capture("Move curve point");
                _drag = Drag.Move;
                _dragPoint = hit;
                return;
            }

            if (isDouble)
            {
                var phase = SnapPhase(XToPhase(pos.X, w));
                var value = YToValue(pos.Y, h);
                var lineY = ValueToY(curve.Evaluate(XToPhase(pos.X, w)), h);
                if (Math.Abs(lineY - pos.Y) <= OnLineSlack) value = curve.Evaluate(phase);

                History?.Capture("Add curve point");
                var point = new AutomationPoint(phase, value);
                curve.Points.Add(point);
                curve.Sort();
                _drag = Drag.Move;
                _dragPoint = point;
                InvalidateVisual();
                return;
            }

            var idx = SegmentIndexAt(XToPhase(pos.X, w), curve);
            if (idx >= 0)
            {
                History?.Capture("Bend curve");
                _drag = Drag.Bend;
                _bendIndex = idx;
                _bendStartCurve = curve.Points[idx].Curve;
                _bendStartY = pos.Y;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var curve = Curve;
            if (_drag == Drag.None || curve is null) { base.OnPointerMoved(e); return; }
            var w = Bounds.Width;
            var h = Bounds.Height;
            var pos = e.GetPosition(this);

            if (_drag == Drag.Move && _dragPoint is not null)
            {
                _dragPoint.Beat = SnapPhase(XToPhase(pos.X, w));
                _dragPoint.Value = YToValue(pos.Y, h);
                curve.Sort();
                InvalidateVisual();
                e.Handled = true;
            }
            else if (_drag == Drag.Bend && _bendIndex >= 0 && _bendIndex < curve.Points.Count)
            {
                if (Math.Abs(pos.Y - _bendStartY) < BendThreshold) return;
                var delta = (_bendStartY - pos.Y) / Math.Max(1.0, h) * 2.0;
                curve.Points[_bendIndex].Curve = Math.Clamp(_bendStartCurve + delta, -1, 1);
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (ReferenceEquals(e.Pointer.Captured, this))
            {
                _drag = Drag.None;
                _dragPoint = null;
                _bendIndex = -1;
                e.Pointer.Capture(null);
                e.Handled = true;
            }

            base.OnPointerReleased(e);
        }

        // --- mapping helpers ---

        private static double PhaseToX(double phase, double w) => Math.Clamp(phase, 0, 1) * w;

        private static double XToPhase(double x, double w) => w > 0 ? Math.Clamp(x / w, 0, 1) : 0;

        private double SnapPhase(double phase)
        {
            var div = SnapDivisions;
            if (div <= 0) return Math.Clamp(phase, 0, 1);
            return Math.Clamp(Math.Round(phase * div) / div, 0, 1);
        }

        private static double ValueToY(double value, double height)
        {
            var t = Math.Clamp(value, 0, 1);
            var top = Pad;
            var bottom = height - Pad;
            return bottom - t * (bottom - top);
        }

        private static double YToValue(double y, double height)
        {
            var top = Pad;
            var bottom = height - Pad;
            return bottom <= top ? 0 : Math.Clamp((bottom - y) / (bottom - top), 0, 1);
        }

        private static double Distance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static AutomationPoint? HitPoint(Point pos, ModulationCurve curve, double w, double h)
        {
            foreach (var p in curve.Points)
            {
                var cx = PhaseToX(p.Beat, w);
                var cy = ValueToY(p.Value, h);
                if (Math.Abs(cx - pos.X) <= HitRadius && Math.Abs(cy - pos.Y) <= HitRadius) return p;
            }

            return null;
        }

        private static int SegmentIndexAt(double phase, ModulationCurve curve)
        {
            var pts = curve.Points;
            if (pts.Count < 2 || phase <= pts[0].Beat) return -1;
            for (var i = 0; i < pts.Count - 1; i++)
                if (phase < pts[i + 1].Beat) return i;
            return -1;
        }
    }
}
