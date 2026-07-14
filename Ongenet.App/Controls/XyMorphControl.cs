using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Ongenet.App.Theming;

namespace Ongenet.App.Controls;

/// <summary>
/// XY morph pad: drag the dot to crossfade four corners. The dot spins while moving.
/// X/Y are 0..1 with Y=0 at the bottom edge and Y=1 at the top.
/// </summary>
public sealed class XyMorphControl : ThemedControl
{
    public static readonly StyledProperty<double> XProperty =
        AvaloniaProperty.Register<XyMorphControl, double>(nameof(X), 0.5);

    public static readonly StyledProperty<double> YProperty =
        AvaloniaProperty.Register<XyMorphControl, double>(nameof(Y), 0.5);

    static XyMorphControl()
    {
        AffectsRender<XyMorphControl>(XProperty, YProperty);
        WidthProperty.OverrideDefaultValue<XyMorphControl>(168);
        HeightProperty.OverrideDefaultValue<XyMorphControl>(168);
        ClipToBoundsProperty.OverrideDefaultValue<XyMorphControl>(true);
    }

    private const double Pad = 10;

    private IBrush _bg = Brushes.Transparent;
    private IPen _border = new Pen(Brushes.Gray);
    private IPen _grid = new Pen(Brushes.Gray);
    private IBrush _dot = Brushes.White;
    private IBrush _dotRing = Brushes.White;
    private IBrush _label = Brushes.Gray;
    private IBrush _labelActive = Brushes.White;

    private readonly DispatcherTimer _animTimer;
    private double _spinAngle;
    private double _spinVelocity;
    private bool _dragging;
    private bool _movedDuringDrag;
    private Point _lastDragPoint;

    public event EventHandler? DragStarted;
    public event EventHandler? DragCompleted;

    public XyMorphControl()
    {
        Focusable = true;
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += OnAnimTick;
    }

    public double X
    {
        get => GetValue(XProperty);
        set => SetValue(XProperty, Math.Clamp(value, 0, 1));
    }

    public double Y
    {
        get => GetValue(YProperty);
        set => SetValue(YProperty, Math.Clamp(value, 0, 1));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _animTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _animTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void BuildThemeResources()
    {
        _bg = new SolidColorBrush(ThemePalette.Crust);
        _border = new Pen(new SolidColorBrush(ThemePalette.Surface2), 1.5);
        _grid = new Pen(new SolidColorBrush(ThemePalette.Surface1), 1);
        _dot = new SolidColorBrush(ThemePalette.Mauve);
        _dotRing = new SolidColorBrush(ThemePalette.Lavender);
        _label = new SolidColorBrush(ThemePalette.Overlay0);
        _labelActive = new SolidColorBrush(ThemePalette.Subtext0);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        _movedDuringDrag = false;
        _lastDragPoint = e.GetPosition(this);
        e.Pointer.Capture(this);
        DragStarted?.Invoke(this, EventArgs.Empty);
        UpdateFromPoint(_lastDragPoint, initial: true);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        UpdateFromPoint(e.GetPosition(this), initial: false);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging) e.Pointer.Capture(null);
        EndDrag();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateFromPoint(Point p, bool initial)
    {
        var inner = GetInnerRect();
        if (inner.Width <= 1 || inner.Height <= 1) return;

        if (!initial)
        {
            var dx = p.X - _lastDragPoint.X;
            var dy = p.Y - _lastDragPoint.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > 0.5)
            {
                _movedDuringDrag = true;
                _spinVelocity = Math.Min(48, _spinVelocity + dist * 0.18);
            }
        }

        _lastDragPoint = p;
        X = Math.Clamp((p.X - inner.X) / inner.Width, 0, 1);
        Y = Math.Clamp(1.0 - (p.Y - inner.Y) / inner.Height, 0, 1);
    }

    private Rect GetInnerRect()
    {
        var rect = new Rect(Bounds.Size);
        return rect.Deflate(Pad);
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        if (!_dragging && Math.Abs(_spinVelocity) < 0.08)
            return;

        if (!_dragging)
            _spinVelocity *= 0.9;
        else if (!_movedDuringDrag)
            _spinVelocity = 0;
        else
            _spinVelocity *= 0.96;

        if (_spinVelocity > 0.08 || (_dragging && _movedDuringDrag))
        {
            _spinAngle += _spinVelocity;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        var inner = rect.Deflate(Pad);

        context.DrawRectangle(_bg, _border, rect, 6);

        var midX = inner.X + inner.Width * 0.5;
        var midY = inner.Y + inner.Height * 0.5;
        context.DrawLine(_grid, new Point(inner.X, midY), new Point(inner.Right, midY));
        context.DrawLine(_grid, new Point(midX, inner.Y), new Point(midX, inner.Bottom));

        DrawCornerLabel(context, "1", inner.X + 4, inner.Bottom - 14, X < 0.5 && Y < 0.5);
        DrawCornerLabel(context, "2", inner.Right - 14, inner.Bottom - 14, X >= 0.5 && Y < 0.5);
        DrawCornerLabel(context, "3", inner.X + 4, inner.Y + 2, X < 0.5 && Y >= 0.5);
        DrawCornerLabel(context, "4", inner.Right - 14, inner.Y + 2, X >= 0.5 && Y >= 0.5);

        var dotX = inner.X + X * inner.Width;
        var dotY = inner.Bottom - Y * inner.Height;
        var spinning = _movedDuringDrag && (_dragging || _spinVelocity > 0.08);

        if (spinning)
        {
            var ringRadius = 10 + Math.Min(6, _spinVelocity * 0.25);
            context.DrawEllipse(null, new Pen(_dotRing, 1.5), new Point(dotX, dotY), ringRadius, ringRadius);
        }

        if (spinning)
        {
            var angle = _spinAngle * Math.PI / 180.0;
            var transform = Matrix.CreateTranslation(new Vector(dotX, dotY))
                * Matrix.CreateRotation(angle);
            using (context.PushTransform(transform))
                DrawDotCore(context);
        }
        else
        {
            DrawDotCore(context, dotX, dotY);
        }
    }

    private void DrawDotCore(DrawingContext context, double cx = 0, double cy = 0)
    {
        context.DrawEllipse(_dot, null, new Point(cx, cy), 7, 7);
        context.DrawLine(new Pen(_dotRing, 2), new Point(cx, cy - 5), new Point(cx, cy - 11));
        context.DrawLine(new Pen(_dotRing, 1.5), new Point(cx + 5, cy), new Point(cx + 9, cy));
    }

    private void DrawCornerLabel(DrawingContext context, string text, double x, double y, bool active)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("fonts:Inter#Inter"), 10, active ? _labelActive : _label);
        context.DrawText(ft, new Point(x, y));
    }
}
