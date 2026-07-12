using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio.Automation;
using Ongenet.App.Services;
using Ongenet.App.Theming;
using Ongenet.App.ViewModels.Timeline;

namespace Ongenet.App.Controls
{
    /// <summary>
    /// The editable automation curve drawn on an automation row. Bound (via DataContext) to an
    /// <see cref="AutomationLaneViewModel"/>, it draws the value polyline (x = beat·PixelsPerBeat,
    /// y from value↔min/max) with point handles, and edits the underlying <see cref="AutomationLane"/>:
    /// click adds a point, dragging a handle moves it, dragging a segment bends its curve
    /// (tension), and right-click deletes a handle. Curve evaluation is shared with
    /// <see cref="AutomationLane.Evaluate"/> so the drawn line matches playback exactly.
    /// </summary>
    public sealed class AutomationLaneControl : ThemedControl, ICustomHitTest
    {
        /// <summary>Bump to force a repaint when the lane's points mutate in place (e.g. while recording).</summary>
        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<AutomationLaneControl, int>(nameof(Revision));

        private const double Pad = 8.0;        // vertical inset so end values aren't clipped
        private const double HandleRadius = 4.0;
        private const double HitRadius = 8.0;         // grab radius for moving an existing handle
        private const double LineHitRadius = 20.0;    // snap a click this close to the stroke onto the curve
        private const double BendThreshold = 3.0;     // px of vertical travel before a segment bends
        private const double ClickMoveTolerance = 5.0; // px before a plain click becomes a drag (not an add)

        private IPen _linePen = new Pen(Brushes.Gray, 1.6);       // accent (mauve)
        private IBrush _handleFill = Brushes.Gray;                // text
        private IPen _handleStroke = new Pen(Brushes.Black, 1);   // base

        protected override void BuildThemeResources()
        {
            _linePen = new Pen(new SolidColorBrush(ThemePalette.Mauve), 1.6);
            _handleFill = new SolidColorBrush(ThemePalette.Text);
            _handleStroke = new Pen(new SolidColorBrush(ThemePalette.Base), 1);
        }

        private enum Drag { None, Move, Bend }

        private static IHistoryService? History => App.ServiceProvider?.GetService<IHistoryService>();

        private Drag _drag = Drag.None;
        private AutomationPoint? _dragPoint;
        private int _bendIndex = -1;
        private double _bendStartCurve;
        private double _bendStartY;

        // A plain click (press + release without dragging) adds a point.
        private Point _pressPos;
        private bool _pendingAdd;
        private bool _dragged;
        private bool _dragHistoryTaken;

        public AutomationLaneControl()
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        }

        // Custom-rendered controls only hit-test drawn pixels by default (our stroke is 1.6px wide).
        protected override Size MeasureOverride(Size availableSize) => availableSize;

        protected override Size ArrangeOverride(Size finalSize) => finalSize;

        bool ICustomHitTest.HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

        static AutomationLaneControl()
        {
            AffectsRender<AutomationLaneControl>(RevisionProperty);
        }

        public int Revision
        {
            get => GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
        }

        private AutomationLaneViewModel? Vm => DataContext as AutomationLaneViewModel;

        public override void Render(DrawingContext context)
        {
            var vm = Vm;
            if (vm is null) return;
            var lane = vm.Lane;
            var m = vm.Metrics;
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 1 || h < 1) return;

            var pts = lane.Points;
            if (pts.Count == 0) return;

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                // Flat hold from x=0 to the first point.
                var first = pts[0];
                var startY = ValueToY(first.Value, lane, h);
                gc.BeginFigure(new Point(0, startY), false);
                gc.LineTo(new Point(BeatToX(first.Beat, m), startY));

                // Each segment, sampled through the shared tension curve.
                for (var i = 0; i < pts.Count - 1; i++)
                {
                    var p0 = pts[i];
                    var p1 = pts[i + 1];
                    var x0 = BeatToX(p0.Beat, m);
                    var x1 = BeatToX(p1.Beat, m);
                    var steps = p0.Curve == 0 ? 1 : (int)Math.Clamp(Math.Abs(x1 - x0) / 6.0, 2, 48);
                    for (var s = 1; s <= steps; s++)
                    {
                        var f = (double)s / steps;
                        var val = p0.Value + (p1.Value - p0.Value) * AutomationLane.Shape(f, p0.Curve);
                        gc.LineTo(new Point(x0 + (x1 - x0) * f, ValueToY(val, lane, h)));
                    }
                }

                // Flat hold from the last point to the right edge.
                var last = pts[pts.Count - 1];
                var lastY = ValueToY(last.Value, lane, h);
                gc.LineTo(new Point(BeatToX(last.Beat, m), lastY));
                gc.LineTo(new Point(w, lastY));
            }

            context.DrawGeometry(null, _linePen, geo);

            foreach (var p in pts)
            {
                var c = new Point(BeatToX(p.Beat, m), ValueToY(p.Value, lane, h));
                context.DrawEllipse(_handleFill, _handleStroke, c, HandleRadius, HandleRadius);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var vm = Vm;
            if (vm is null) { base.OnPointerPressed(e); return; }
            var lane = vm.Lane;
            var m = vm.Metrics;
            var h = Bounds.Height;
            var pos = e.GetPosition(this);
            var props = e.GetCurrentPoint(this).Properties;

            // Right-click a handle: delete it (keep at least one point).
            if (props.IsRightButtonPressed)
            {
                var victim = HitPoint(pos, lane, m, h);
                if (victim is not null && lane.Points.Count > 1)
                {
                    History?.Capture("Delete automation point");
                    lane.RemovePoint(victim);
                    vm.CommitEdits();
                    InvalidateVisual();
                    e.Handled = true;
                }

                return;
            }

            if (!props.IsLeftButtonPressed) { base.OnPointerPressed(e); return; }

            // Always own left clicks on the lane and capture the pointer, so the surrounding ListBox
            // never intercepts them (its selection handling otherwise eats clicks in the flat regions).
            e.Handled = true;
            e.Pointer.Capture(this);

            _pressPos = pos;
            _pendingAdd = true;
            _dragged = false;
            _dragHistoryTaken = false;

            var handle = HitPoint(pos, lane, m, h);
            if (handle is not null)
            {
                _drag = Drag.Move;
                _dragPoint = handle;
                _pendingAdd = false;
                return;
            }

            // Click on a segment: drag vertically to bend; a plain click still adds a point on release.
            var idx = SegmentIndexAt(XToBeat(pos.X, m), lane);
            if (idx >= 0)
            {
                _drag = Drag.Bend;
                _bendIndex = idx;
                _bendStartCurve = lane.Points[idx].Curve;
                _bendStartY = pos.Y;
            }
            else
            {
                _drag = Drag.None;
            }
        }

        private static double Distance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var vm = Vm;
            if (vm is null) { base.OnPointerMoved(e); return; }
            if (_drag == Drag.None && !_pendingAdd) { base.OnPointerMoved(e); return; }
            var lane = vm.Lane;
            var m = vm.Metrics;
            var h = Bounds.Height;
            var pos = e.GetPosition(this);

            if (_drag == Drag.Move && _dragPoint is not null)
            {
                _dragged = true;
                _pendingAdd = false;
                if (!_dragHistoryTaken) { History?.Capture("Move automation point"); _dragHistoryTaken = true; }
                _dragPoint.Beat = Math.Max(0, m.Snap(XToBeat(pos.X, m)));
                _dragPoint.Value = YToValue(pos.Y, lane, h);
                lane.Sort();
                vm.CommitEdits();
                InvalidateVisual();
                e.Handled = true;
            }
            else if (_drag == Drag.Bend && _bendIndex >= 0 && _bendIndex < lane.Points.Count)
            {
                if (Math.Abs(pos.Y - _bendStartY) < BendThreshold) return;
                _dragged = true;
                _pendingAdd = false;
                if (!_dragHistoryTaken) { History?.Capture("Bend automation"); _dragHistoryTaken = true; }
                var delta = (_bendStartY - pos.Y) / Math.Max(1.0, h) * 2.0;
                lane.Points[_bendIndex].Curve = Math.Clamp(_bendStartCurve + delta, -1, 1);
                vm.CommitEdits();
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
            var vm = Vm;
            if (vm is null) return;
            var lane = vm.Lane;
            var m = vm.Metrics;
            var h = Bounds.Height;

            // Snap onto the drawn curve when the click is near it, so extending the line needs no precise aim.
            double beat;
            double value;
            if (TryNearestOnCurve(pos, lane, m, h, w: Bounds.Width, out var nearestBeat, out var nearestValue))
            {
                beat = nearestBeat;
                value = nearestValue;
            }
            else
            {
                beat = Math.Max(0, m.Snap(XToBeat(pos.X, m)));
                value = YToValue(pos.Y, lane, h);
            }

            History?.Capture("Add automation point");
            lane.AddPoint(new AutomationPoint(beat, value));
            vm.CommitEdits();
            InvalidateVisual();
        }

        // --- mapping helpers ---

        private static double BeatToX(double beat, TimelineMetrics m) => beat * m.PixelsPerBeat;

        private static double XToBeat(double x, TimelineMetrics m) => m.PixelsPerBeat > 0 ? x / m.PixelsPerBeat : 0;

        private static double ValueToY(double value, AutomationLane lane, double height)
        {
            var range = lane.Maximum - lane.Minimum;
            var t = range <= 0 ? 0.5 : Math.Clamp((value - lane.Minimum) / range, 0, 1);
            var top = Pad;
            var bottom = height - Pad;
            return bottom - t * (bottom - top);
        }

        private static double YToValue(double y, AutomationLane lane, double height)
        {
            var top = Pad;
            var bottom = height - Pad;
            var t = bottom <= top ? 0 : Math.Clamp((bottom - y) / (bottom - top), 0, 1);
            return lane.Minimum + t * (lane.Maximum - lane.Minimum);
        }

        private static AutomationPoint? HitPoint(Point pos, AutomationLane lane, TimelineMetrics m, double height)
        {
            AutomationPoint? best = null;
            var bestDist = HitRadius;
            foreach (var p in lane.Points)
            {
                var d = Distance(pos, HandleCenter(p, lane, m, height));
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }

            return best;
        }

        private static Point HandleCenter(AutomationPoint p, AutomationLane lane, TimelineMetrics m, double height)
            => new(BeatToX(p.Beat, m), ValueToY(p.Value, lane, height));

        /// <summary>Minimum distance from <paramref name="pos"/> to the drawn curve polyline.</summary>
        private static double DistanceToCurve(Point pos, AutomationLane lane, TimelineMetrics m, double height,
            double w, out Point closest)
        {
            closest = default;
            BuildCurvePolyline(lane, m, height, w, out var polyline);
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

        /// <summary>
        /// Finds the nearest point on the drawn curve polyline (same sampling as <see cref="Render"/>).
        /// Returns true when within <see cref="LineHitRadius"/> of the stroke.
        /// </summary>
        private static bool TryNearestOnCurve(Point pos, AutomationLane lane, TimelineMetrics m, double height,
            double w, out double beat, out double value)
        {
            beat = 0;
            value = 0;
            var dist = DistanceToCurve(pos, lane, m, height, w, out var closest);
            if (dist > LineHitRadius) return false;

            beat = Math.Max(0, m.Snap(XToBeat(closest.X, m)));
            value = lane.Evaluate(beat);
            return true;
        }

        private static void BuildCurvePolyline(AutomationLane lane, TimelineMetrics m, double height, double w,
            out System.Collections.Generic.List<Point> polyline)
        {
            polyline = new System.Collections.Generic.List<Point>();
            var pts = lane.Points;
            if (pts.Count == 0) return;

            var first = pts[0];
            var startY = ValueToY(first.Value, lane, height);
            polyline.Add(new Point(0, startY));
            polyline.Add(new Point(BeatToX(first.Beat, m), startY));

            for (var i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[i];
                var p1 = pts[i + 1];
                var x0 = BeatToX(p0.Beat, m);
                var x1 = BeatToX(p1.Beat, m);
                var steps = p0.Curve == 0 ? 1 : (int)Math.Clamp(Math.Abs(x1 - x0) / 6.0, 2, 48);
                for (var s = 1; s <= steps; s++)
                {
                    var f = (double)s / steps;
                    var val = p0.Value + (p1.Value - p0.Value) * AutomationLane.Shape(f, p0.Curve);
                    polyline.Add(new Point(x0 + (x1 - x0) * f, ValueToY(val, lane, height)));
                }
            }

            var last = pts[pts.Count - 1];
            var lastY = ValueToY(last.Value, lane, height);
            polyline.Add(new Point(BeatToX(last.Beat, m), lastY));
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

        // Index of the segment (point i → i+1) covering the given beat, or -1 in the flat end regions.
        private static int SegmentIndexAt(double beat, AutomationLane lane)
        {
            var pts = lane.Points;
            if (pts.Count < 2 || beat <= pts[0].Beat) return -1;
            for (var i = 0; i < pts.Count - 1; i++)
            {
                if (beat < pts[i + 1].Beat) return i;
            }

            return -1;
        }
    }
}
