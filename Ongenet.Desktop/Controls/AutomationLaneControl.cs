using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ongenet.Core.Audio.Automation;
using Ongenet.Desktop.ViewModels.Timeline;

namespace Ongenet.Desktop.Controls
{
    /// <summary>
    /// The editable automation curve drawn on an automation row. Bound (via DataContext) to an
    /// <see cref="AutomationLaneViewModel"/>, it draws the value polyline (x = beat·PixelsPerBeat,
    /// y from value↔min/max) with point handles, and edits the underlying <see cref="AutomationLane"/>:
    /// double-click adds a point, dragging a handle moves it, dragging a segment bends its curve
    /// (tension), and right-click deletes a handle. Curve evaluation is shared with
    /// <see cref="AutomationLane.Evaluate"/> so the drawn line matches playback exactly.
    /// </summary>
    public sealed class AutomationLaneControl : Control
    {
        /// <summary>Bump to force a repaint when the lane's points mutate in place (e.g. while recording).</summary>
        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<AutomationLaneControl, int>(nameof(Revision));

        private const double Pad = 8.0;        // vertical inset so end values aren't clipped
        private const double HandleRadius = 4.0;
        private const double HitRadius = 7.0;
        private const double OnLineSlack = 6.0; // double-clicking within this of the curve drops the point onto it
        private const double BendThreshold = 3.0; // px of vertical travel before a segment actually bends

        private static readonly IPen LinePen = new Pen(new SolidColorBrush(Color.Parse("#cba6f7")), 1.6);
        private static readonly IBrush HandleFill = new SolidColorBrush(Color.Parse("#cdd6f4"));
        private static readonly IPen HandleStroke = new Pen(new SolidColorBrush(Color.Parse("#1e1e2e")), 1);

        private enum Drag { None, Move, Bend }

        private Drag _drag = Drag.None;
        private AutomationPoint? _dragPoint;
        private int _bendIndex = -1;
        private double _bendStartCurve;
        private double _bendStartY;

        // Manual double-click detection — the framework's ClickCount is unreliable here because the
        // lane lives inside a ListBox whose selection handling resets the OS click counter between
        // clicks. We track the previous left-press time/position ourselves instead.
        private const double DoubleClickMs = 400.0;
        private const double DoubleClickSlop = 8.0; // px the second click may stray from the first
        private long _lastPressTick;
        private Point _lastPressPos;

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

            context.DrawGeometry(null, LinePen, geo);

            foreach (var p in pts)
            {
                var c = new Point(BeatToX(p.Beat, m), ValueToY(p.Value, lane, h));
                context.DrawEllipse(HandleFill, HandleStroke, c, HandleRadius, HandleRadius);
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
                    lane.RemovePoint(victim);
                    vm.CommitEdits();
                    InvalidateVisual();
                    e.Handled = true;
                }

                return;
            }

            if (!props.IsLeftButtonPressed) { base.OnPointerPressed(e); return; }

            // Always own left clicks on the lane and capture the pointer, so the surrounding ListBox
            // never intercepts them (its selection handling was eating clicks and breaking double-click
            // detection — especially in the flat regions where the curve had nothing under the cursor).
            e.Handled = true;
            e.Pointer.Capture(this);

            // Manual double-click test against the previous press.
            var now = Environment.TickCount64;
            var isDouble = now - _lastPressTick <= DoubleClickMs && Distance(pos, _lastPressPos) <= DoubleClickSlop;
            _lastPressTick = isDouble ? 0 : now; // consume, so a triple-click doesn't re-fire
            _lastPressPos = pos;

            // A handle under the cursor is always a move (or, on the second click, lets you re-grab it).
            var hit = HitPoint(pos, lane, m, h);
            if (hit is not null)
            {
                _drag = Drag.Move;
                _dragPoint = hit;
                return;
            }

            if (isDouble)
            {
                // Add a point (snapped in time, value from Y). If the click is within a few pixels of the
                // existing curve, drop it ONTO the line so extending the curve doesn't need precise aim.
                var beat = Math.Max(0, m.Snap(XToBeat(pos.X, m)));
                var value = YToValue(pos.Y, lane, h);
                var lineY = ValueToY(lane.Evaluate(XToBeat(pos.X, m)), lane, h);
                if (Math.Abs(lineY - pos.Y) <= OnLineSlack) value = lane.Evaluate(beat);

                var point = new AutomationPoint(beat, value);
                lane.AddPoint(point);
                vm.CommitEdits();
                _drag = Drag.Move; // let the user keep dragging the new point
                _dragPoint = point;
                InvalidateVisual();
                return;
            }

            // Single click on a segment: drag vertically to bend its curve (tension). In the flat end
            // regions there's no segment — the click is still owned (so a following click double-fires).
            var idx = SegmentIndexAt(XToBeat(pos.X, m), lane);
            if (idx >= 0)
            {
                _drag = Drag.Bend;
                _bendIndex = idx;
                _bendStartCurve = lane.Points[idx].Curve;
                _bendStartY = pos.Y;
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
            if (_drag == Drag.None || vm is null) { base.OnPointerMoved(e); return; }
            var lane = vm.Lane;
            var m = vm.Metrics;
            var h = Bounds.Height;
            var pos = e.GetPosition(this);

            if (_drag == Drag.Move && _dragPoint is not null)
            {
                _dragPoint.Beat = Math.Max(0, m.Snap(XToBeat(pos.X, m)));
                _dragPoint.Value = YToValue(pos.Y, lane, h);
                lane.Sort();
                vm.CommitEdits();
                InvalidateVisual();
                e.Handled = true;
            }
            else if (_drag == Drag.Bend && _bendIndex >= 0 && _bendIndex < lane.Points.Count)
            {
                // Ignore tiny travel so a quick double-click (or stray click) doesn't bend the segment.
                if (Math.Abs(pos.Y - _bendStartY) < BendThreshold) return;
                // Dragging up increases tension toward ease-out; down toward ease-in.
                var delta = (_bendStartY - pos.Y) / Math.Max(1.0, h) * 2.0;
                lane.Points[_bendIndex].Curve = Math.Clamp(_bendStartCurve + delta, -1, 1);
                vm.CommitEdits();
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            // We capture on every left press (including no-op clicks in the flat regions), so always
            // release here — not just when a drag was in progress — or the pointer stays captured.
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
            foreach (var p in lane.Points)
            {
                var cx = BeatToX(p.Beat, m);
                var cy = ValueToY(p.Value, lane, height);
                if (Math.Abs(cx - pos.X) <= HitRadius && Math.Abs(cy - pos.Y) <= HitRadius) return p;
            }

            return null;
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
