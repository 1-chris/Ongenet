using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
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
    public sealed class CurveEditorControl : ThemedControl, ICustomHitTest
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
        private const double LineHitRadius = 20.0;
        private const double BendThreshold = 3.0;
        private const double ClickMoveTolerance = 5.0;

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
        private Point _pressPos;
        private bool _pendingAdd;
        private bool _dragged;
        private bool _dragHistoryTaken;

        public CurveEditorControl()
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        }

        protected override Size MeasureOverride(Size availableSize) => availableSize;

        protected override Size ArrangeOverride(Size finalSize) => finalSize;

        bool ICustomHitTest.HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

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

            _pressPos = pos;
            _pendingAdd = true;
            _dragged = false;
            _dragHistoryTaken = false;

            var hit = HitPoint(pos, curve, w, h);
            if (hit is not null)
            {
                _drag = Drag.Move;
                _dragPoint = hit;
                _pendingAdd = false;
                return;
            }

            var idx = SegmentIndexAt(XToPhase(pos.X, w), curve);
            if (idx >= 0)
            {
                _drag = Drag.Bend;
                _bendIndex = idx;
                _bendStartCurve = curve.Points[idx].Curve;
                _bendStartY = pos.Y;
            }
            else
            {
                _drag = Drag.None;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var curve = Curve;
            if (curve is null) { base.OnPointerMoved(e); return; }
            if (_drag == Drag.None && !_pendingAdd) { base.OnPointerMoved(e); return; }
            var w = Bounds.Width;
            var h = Bounds.Height;
            var pos = e.GetPosition(this);

            if (_drag == Drag.Move && _dragPoint is not null)
            {
                _dragged = true;
                _pendingAdd = false;
                if (!_dragHistoryTaken) { History?.Capture("Move curve point"); _dragHistoryTaken = true; }
                _dragPoint.Beat = SnapPhase(XToPhase(pos.X, w));
                _dragPoint.Value = YToValue(pos.Y, h);
                curve.Sort();
                InvalidateVisual();
                e.Handled = true;
            }
            else if (_drag == Drag.Bend && _bendIndex >= 0 && _bendIndex < curve.Points.Count)
            {
                if (Math.Abs(pos.Y - _bendStartY) < BendThreshold) return;
                _dragged = true;
                _pendingAdd = false;
                if (!_dragHistoryTaken) { History?.Capture("Bend curve"); _dragHistoryTaken = true; }
                var delta = (_bendStartY - pos.Y) / Math.Max(1.0, h) * 2.0;
                curve.Points[_bendIndex].Curve = Math.Clamp(_bendStartCurve + delta, -1, 1);
                InvalidateVisual();
                e.Handled = true;
            }
            else if (_pendingAdd && Distance(pos, _pressPos) > ClickMoveTolerance)
            {
                _dragged = true;
                _pendingAdd = false;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (ReferenceEquals(e.Pointer.Captured, this))
            {
                if (_pendingAdd && !_dragged)
                    AddPointAt(_pressPos);

                _drag = Drag.None;
                _dragPoint = null;
                _bendIndex = -1;
                _pendingAdd = false;
                e.Pointer.Capture(null);
                e.Handled = true;
            }

            base.OnPointerReleased(e);
        }

        private void AddPointAt(Point pos)
        {
            var curve = Curve;
            if (curve is null) return;
            var w = Bounds.Width;
            var h = Bounds.Height;

            double phase;
            double value;
            if (TryNearestOnCurve(pos, curve, w, h, out var nearestPhase, out var nearestValue))
            {
                phase = nearestPhase;
                value = nearestValue;
            }
            else
            {
                phase = SnapPhase(XToPhase(pos.X, w));
                value = YToValue(pos.Y, h);
            }

            History?.Capture("Add curve point");
            curve.Points.Add(new AutomationPoint(phase, value));
            curve.Sort();
            InvalidateVisual();
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
            AutomationPoint? best = null;
            var bestDist = HitRadius;
            foreach (var p in curve.Points)
            {
                var d = Distance(pos, HandleCenter(p, w, h));
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }

            return best;
        }

        private static Point HandleCenter(AutomationPoint p, double w, double h)
            => new(PhaseToX(p.Beat, w), ValueToY(p.Value, h));

        private static double DistanceToCurve(Point pos, ModulationCurve curve, double w, double h, out Point closest)
        {
            closest = default;
            BuildCurvePolyline(curve, w, h, out var polyline);
            if (polyline.Count < 2) return double.PositiveInfinity;

            var bestDist = double.PositiveInfinity;
            for (var i = 1; i < polyline.Count; i++)
            {
                var d = DistanceToSegment(pos, polyline[i - 1], polyline[i], out var c);
                if (d < bestDist)
                {
                    bestDist = d;
                    closest = c;
                }
            }

            return bestDist;
        }

        private bool TryNearestOnCurve(Point pos, ModulationCurve curve, double w, double h,
            out double phase, out double value)
        {
            phase = 0;
            value = 0;
            var dist = DistanceToCurve(pos, curve, w, h, out var closest);
            if (dist > LineHitRadius) return false;

            phase = SnapPhase(XToPhase(closest.X, w));
            value = curve.Evaluate(phase);
            return true;
        }

        private static void BuildCurvePolyline(ModulationCurve curve, double w, double h,
            out System.Collections.Generic.List<Point> polyline)
        {
            polyline = new System.Collections.Generic.List<Point>();
            var pts = curve.Points;
            if (pts.Count == 0) return;

            var first = pts[0];
            var startY = ValueToY(first.Value, h);
            polyline.Add(new Point(0, startY));
            polyline.Add(new Point(PhaseToX(first.Beat, w), startY));

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
                    polyline.Add(new Point(x0 + (x1 - x0) * f, ValueToY(val, h)));
                }
            }

            var last = pts[pts.Count - 1];
            var lastY = ValueToY(last.Value, h);
            polyline.Add(new Point(PhaseToX(last.Beat, w), lastY));
            polyline.Add(new Point(w, lastY));
        }

        private static double DistanceToSegment(Point p, Point a, Point b, out Point closest)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lenSq = dx * dx + dy * dy;
            if (lenSq <= 0)
            {
                closest = a;
                return Distance(p, a);
            }

            var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
            closest = new Point(a.X + t * dx, a.Y + t * dy);
            return Distance(p, closest);
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
