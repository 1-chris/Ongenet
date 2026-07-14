using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Ongenet.App.Localization;
using Ongenet.App.Services;
using Ongenet.App.Theming;
using Ongenet.App.ViewModels.Field;
using Ongenet.Core.Audio.Field;

namespace Ongenet.App.Controls.Field;

/// <summary>
/// The Field node-graph canvas: a custom-drawn, zoomable/pannable editor that renders the graph's nodes,
/// ports and patch cords and handles all interaction (pan/zoom, select and drag nodes, draw/remove
/// connections, and right-click menus to add nodes / route modulation). It reads and mutates the live
/// <see cref="FieldGraph"/> from its <see cref="FieldEditorViewModel"/> data context and asks the VM to
/// recompile after structural edits. Theme-aware via <see cref="ThemedControl"/>.
/// </summary>
public sealed class FieldCanvasControl : ThemedControl
{
    private const double NodeWidth = 168;
    private const double HeaderHeight = 26;
    private const double RowHeight = 20;
    private const double PortRadius = 5;
    private const double BodyPadding = 8;
    private const double DefaultVisualHeight = 130;
    private const double ResizeHandle = 14;

    private FieldEditorViewModel? _vm;

    // View transform (world -> screen): screen = world * zoom + pan.
    private double PanX { get => _vm?.Graph.ViewX ?? 0; set { if (_vm is not null) _vm.Graph.ViewX = value; } }
    private double PanY { get => _vm?.Graph.ViewY ?? 0; set { if (_vm is not null) _vm.Graph.ViewY = value; } }
    private double Zoom { get => _vm?.Graph.Zoom is > 0.05 and < 8 ? _vm.Graph.Zoom : 1.0; set { if (_vm is not null) _vm.Graph.Zoom = value; } }

    private enum Mode { None, Pan, DragNode, Connect, Resize }
    private Mode _mode;
    private Point _lastPointer;
    private FieldNode? _dragNode;
    private Point _dragOffset;   // world offset from node origin to grab point
    private FieldNode? _connectNode;
    private FieldPort? _connectPort;
    private Point _connectCurrent;
    private FieldNode? _resizeNode;

    /// <summary>Raised when the view transform, a node position/size, or the graph structure changes, so an
    /// overlay (the on-graph visualizations) can reposition.</summary>
    public event Action? ViewChanged;

    private readonly DispatcherTimer _timer;

    // Theme brushes/pens.
    private IBrush _bg = Brushes.Black;
    private IPen _grid = new Pen(Brushes.Gray);
    private IBrush _nodeFill = Brushes.DimGray;
    private IBrush _headerFill = Brushes.Gray;
    private IPen _nodeBorder = new Pen(Brushes.Gray);
    private IPen _selBorder = new Pen(Brushes.Magenta, 2);
    private IBrush _text = Brushes.White;
    private IBrush _audioPort = Brushes.Orange;
    private IBrush _cvPort = Brushes.LightBlue;
    private IBrush _notePort = Brushes.LightGreen;
    private IPen _wire = new Pen(Brushes.Orange, 2);
    private IPen _wireCv = new Pen(Brushes.LightBlue, 1.5);
    private IBrush _assetPort = Brushes.Violet;
    private IPen _wireAsset = new Pen(Brushes.Violet, 1.5) { DashStyle = DashStyle.Dash };

    public FieldCanvasControl()
    {
        Focusable = true;
        ClipToBounds = true;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(UiPerfProfile.AnalyserIntervalMs)
        };
        _timer.Tick += (_, _) =>
        {
            if (FrameTicker.IsEffectivelyVisible(this) && HasScope())
                InvalidateVisual();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.StructureChanged -= OnStructureChanged;
        _vm = DataContext as FieldEditorViewModel;
        if (_vm is not null) _vm.StructureChanged += OnStructureChanged;
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnStructureChanged()
    {
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var r = base.ArrangeOverride(finalSize);
        ViewChanged?.Invoke();
        return r;
    }

    protected override void BuildThemeResources()
    {
        _bg = new SolidColorBrush(ThemePalette.Mantle);
        _grid = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Surface0, 160)), 1);
        _nodeFill = new SolidColorBrush(ThemePalette.Surface0);
        _headerFill = new SolidColorBrush(ThemePalette.Surface1);
        _nodeBorder = new Pen(new SolidColorBrush(ThemePalette.Surface2), 1);
        _selBorder = new Pen(new SolidColorBrush(ThemePalette.Mauve), 2);
        _text = new SolidColorBrush(ThemePalette.Text);
        _audioPort = new SolidColorBrush(ThemePalette.Peach);
        _cvPort = new SolidColorBrush(ThemePalette.Sky);
        _notePort = new SolidColorBrush(ThemePalette.Green);
        _wire = new Pen(new SolidColorBrush(ThemePalette.Peach), 2);
        _wireCv = new Pen(new SolidColorBrush(ThemePalette.Sky), 1.5);
        _assetPort = new SolidColorBrush(ThemePalette.Mauve);
        _wireAsset = new Pen(new SolidColorBrush(ThemePalette.Mauve), 1.5) { DashStyle = DashStyle.Dash };
    }

    // ---- Geometry ----

    private Point WorldToScreen(double x, double y) => new(x * Zoom + PanX, y * Zoom + PanY);
    private Point ScreenToWorld(Point p) => new((p.X - PanX) / Zoom, (p.Y - PanY) / Zoom);

    private static double NodeW(FieldNode n) => n.Width > 40 ? n.Width : NodeWidth;

    private static double PortRowsHeight(FieldNode n)
        => HeaderHeight + Math.Max(n.Inputs.Count, n.Outputs.Count) * RowHeight + BodyPadding;

    private static double VisualAreaHeight(FieldNode n)
        => n.HasVisual ? DefaultVisualHeight + Math.Max(0, n.VisualHeight) : Math.Max(0, n.VisualHeight);

    private static double NodeHeight(FieldNode n) => PortRowsHeight(n) + VisualAreaHeight(n);

    private Point InputPortWorld(FieldNode n, int i) => new(n.X, n.Y + HeaderHeight + i * RowHeight + RowHeight / 2);
    private Point OutputPortWorld(FieldNode n, int i) => new(n.X + NodeW(n), n.Y + HeaderHeight + i * RowHeight + RowHeight / 2);

    /// <summary>The world-space rectangle of a node's visual area (below its ports), or empty if it has none.</summary>
    private Rect VisualAreaWorld(FieldNode n)
    {
        var h = VisualAreaHeight(n);
        if (h <= 0) return default;
        var top = n.Y + PortRowsHeight(n) - BodyPadding;
        return new Rect(n.X + 6, top, NodeW(n) - 12, h - 6);
    }

    /// <summary>The screen-space rectangle of a node's visual area, for the overlay host. False if none/off-screen.</summary>
    public bool TryGetVisualRect(FieldNode n, out Rect screenRect)
    {
        screenRect = default;
        var w = VisualAreaWorld(n);
        if (w.Width <= 0) return false;
        var tl = WorldToScreen(w.X, w.Y);
        screenRect = new Rect(tl.X, tl.Y, w.Width * Zoom, w.Height * Zoom);
        return true;
    }

    private bool HasScope()
    {
        if (_vm is null) return false;
        foreach (var n in _vm.Graph.Nodes) if (n.HasVisual) return true;
        return false;
    }

    // ---- Rendering ----

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        ctx.FillRectangle(_bg, new Rect(Bounds.Size));
        if (_vm is null) return;

        DrawGrid(ctx);

        // Connections.
        foreach (var conn in _vm.VisibleConnections())
        {
            var src = _vm.Graph.FindNode(conn.SourceNode);
            var dst = _vm.Graph.FindNode(conn.DestNode);
            if (src is null || dst is null) continue;
            var si = IndexOfOutput(src, conn.SourcePort);
            var di = IndexOfInput(dst, conn.DestPort);
            if (si < 0 || di < 0) continue;
            var a = WorldToScreen(OutputPortWorld(src, si).X, OutputPortWorld(src, si).Y);
            var b = WorldToScreen(InputPortWorld(dst, di).X, InputPortWorld(dst, di).Y);
            var pen = src.Outputs[si].Kind switch
            {
                FieldSignalKind.Audio => _wire,
                FieldSignalKind.Asset => _wireAsset,
                _ => _wireCv
            };
            DrawWire(ctx, a, b, pen);
        }

        // In-progress connection.
        if (_mode == Mode.Connect && _connectNode is not null && _connectPort is not null)
        {
            var start = _connectPort.Direction == FieldPortDirection.Output
                ? OutputPortWorld(_connectNode, IndexOfOutput(_connectNode, _connectPort.Id))
                : InputPortWorld(_connectNode, IndexOfInput(_connectNode, _connectPort.Id));
            DrawWire(ctx, WorldToScreen(start.X, start.Y), _connectCurrent, _wire);
        }

        // Groups (root view only).
        if (_vm.NavigationGroupId is null)
            foreach (var group in _vm.Graph.Groups) DrawGroup(ctx, group);

        // Nodes.
        foreach (var n in _vm.VisibleNodes()) DrawNode(ctx, n);
    }

    private void DrawGrid(DrawingContext ctx)
    {
        var spacing = 25.0 * Zoom;
        if (spacing < 6) return;
        var startX = PanX % spacing;
        var startY = PanY % spacing;
        for (var x = startX; x < Bounds.Width; x += spacing)
            ctx.DrawLine(_grid, new Point(x, 0), new Point(x, Bounds.Height));
        for (var y = startY; y < Bounds.Height; y += spacing)
            ctx.DrawLine(_grid, new Point(0, y), new Point(Bounds.Width, y));
    }

    private void DrawGroup(DrawingContext ctx, FieldGroup group)
    {
        if (!TryGetGroupBounds(group, out var bounds)) return;
        var tl = WorldToScreen(bounds.X, bounds.Y);
        var rect = new Rect(tl.X, tl.Y, bounds.Width * Zoom, bounds.Height * Zoom);
        ctx.DrawRectangle(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Surface1, 120)),
            new Pen(new SolidColorBrush(ThemePalette.Mauve), 1.5), rect, 8, 8);
        if (Zoom > 0.45)
            DrawText(ctx, group.Name, new Point(tl.X + 8 * Zoom, tl.Y + 4 * Zoom), 11 * Zoom,
                new SolidColorBrush(ThemePalette.Mauve));
    }

    private bool TryGetGroupBounds(FieldGroup group, out Rect bounds)
    {
        bounds = default;
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var any = false;
        foreach (var node in _vm!.Graph.Nodes)
        {
            if (!group.NodeIds.Contains(node.Id)) continue;
            any = true;
            minX = Math.Min(minX, node.X);
            minY = Math.Min(minY, node.Y);
            maxX = Math.Max(maxX, node.X + NodeW(node));
            maxY = Math.Max(maxY, node.Y + NodeHeight(node));
        }

        if (!any) return false;
        const double pad = 18;
        bounds = new Rect(minX - pad, minY - pad, maxX - minX + pad * 2, maxY - minY + pad * 2);
        return true;
    }

    private bool HitGroup(Point screen, out FieldGroup? group)
    {
        group = null;
        if (_vm is null || _vm.NavigationGroupId is not null) return false;
        foreach (var g in _vm.Graph.Groups)
        {
            if (!TryGetGroupBounds(g, out var bounds)) continue;
            var tl = WorldToScreen(bounds.X, bounds.Y);
            var rect = new Rect(tl.X, tl.Y, bounds.Width * Zoom, bounds.Height * Zoom);
            if (rect.Contains(screen)) { group = g; return true; }
        }
        return false;
    }

    private void DrawNode(DrawingContext ctx, FieldNode n)
    {
        var origin = WorldToScreen(n.X, n.Y);
        var w = NodeW(n) * Zoom;
        var h = NodeHeight(n) * Zoom;
        var rect = new Rect(origin.X, origin.Y, w, h);
        var radius = 6 * Zoom;

        ctx.DrawRectangle(_nodeFill, ReferenceEquals(n, _vm?.SelectedNode) ? _selBorder : _nodeBorder,
            rect, radius, radius);
        var header = new Rect(origin.X, origin.Y, w, HeaderHeight * Zoom);
        ctx.DrawRectangle(_headerFill, null, header, radius, radius);

        if (Zoom > 0.4)
        {
            DrawText(ctx, n.DisplayName, new Point(origin.X + 8 * Zoom, origin.Y + 5 * Zoom), 12 * Zoom, _text);

            for (var i = 0; i < n.Inputs.Count; i++)
            {
                var p = WorldToScreen(InputPortWorld(n, i).X, InputPortWorld(n, i).Y);
                var port = n.Inputs[i];
                ctx.DrawEllipse(PortBrush(port.Kind), null, p, PortRadius * Zoom, PortRadius * Zoom);
                if (Zoom > 0.7)
                    DrawText(ctx, port.IsModulation ? "~" + port.DisplayName : port.DisplayName,
                        new Point(p.X + 8 * Zoom, p.Y - 8 * Zoom), 10 * Zoom, _text);
            }

            for (var i = 0; i < n.Outputs.Count; i++)
            {
                var p = WorldToScreen(OutputPortWorld(n, i).X, OutputPortWorld(n, i).Y);
                ctx.DrawEllipse(PortBrush(n.Outputs[i].Kind), null, p, PortRadius * Zoom, PortRadius * Zoom);
                if (Zoom > 0.7)
                    DrawText(ctx, n.Outputs[i].DisplayName,
                        new Point(p.X - 8 * Zoom - MeasureWidth(n.Outputs[i].DisplayName, 10 * Zoom), p.Y - 8 * Zoom), 10 * Zoom, _text);
            }

            // Visual area frame (the live GPU visual is drawn by the editor's overlay on top of this).
            if (n.HasVisual || n.VisualHeight > 0)
            {
                var va = VisualAreaWorld(n);
                var vtl = WorldToScreen(va.X, va.Y);
                var varect = new Rect(vtl.X, vtl.Y, va.Width * Zoom, va.Height * Zoom);
                ctx.DrawRectangle(new SolidColorBrush(ThemePalette.Crust), _nodeBorder, varect, 3, 3);
            }
        }

        // Resize handle (bottom-right).
        var hs = ResizeHandle * Zoom;
        var handle = new Rect(rect.Right - hs, rect.Bottom - hs, hs, hs);
        var hg = new StreamGeometry();
        using (var g = hg.Open())
        {
            g.BeginFigure(new Point(handle.Right, handle.Top), true);
            g.LineTo(new Point(handle.Right, handle.Bottom));
            g.LineTo(new Point(handle.Left, handle.Bottom));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(ThemePalette.Overlay0), null, hg);
    }

    private IBrush PortBrush(FieldSignalKind kind) => kind switch
    {
        FieldSignalKind.Cv => _cvPort,
        FieldSignalKind.Note => _notePort,
        FieldSignalKind.Asset => _assetPort,
        _ => _audioPort
    };

    private static void DrawWire(DrawingContext ctx, Point a, Point b, IPen pen)
    {
        var dx = Math.Max(30, Math.Abs(b.X - a.X) * 0.5);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(a, false);
            g.CubicBezierTo(new Point(a.X + dx, a.Y), new Point(b.X - dx, b.Y), b);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }

    private void DrawText(DrawingContext ctx, string text, Point at, double size, IBrush brush)
    {
        if (size < 5) return;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush);
        ctx.DrawText(ft, at);
    }

    private static double MeasureWidth(string text, double size)
    {
        if (size < 5) return 0;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, Brushes.White);
        return ft.Width;
    }

    private static int IndexOfInput(FieldNode n, string id)
    {
        for (var i = 0; i < n.Inputs.Count; i++) if (n.Inputs[i].Id == id) return i;
        return -1;
    }

    private static int IndexOfOutput(FieldNode n, string id)
    {
        for (var i = 0; i < n.Outputs.Count; i++) if (n.Outputs[i].Id == id) return i;
        return -1;
    }

    // ---- Interaction ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_vm is null) return;
        var pt = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        _lastPointer = pt;

        if (props.IsMiddleButtonPressed)
        {
            _mode = Mode.Pan;
            e.Handled = true;
            return;
        }

        if (props.IsRightButtonPressed)
        {
            HandleRightClick(pt);
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        // Resize handle hit?
        if (HitResize(pt, out var resizeNode))
        {
            _vm.SelectedNode = resizeNode;
            _mode = Mode.Resize;
            _resizeNode = resizeNode;
            e.Handled = true;
            return;
        }

        // Port hit?
        if (HitPort(pt, out var node, out var port))
        {
            if (port!.Direction == FieldPortDirection.Input)
            {
                _vm.CaptureHistory("Wire Field node");
                _vm.Graph.DisconnectPort(node!.Id, port.Id);
            }
            _mode = Mode.Connect;
            _connectNode = node;
            _connectPort = port;
            _connectCurrent = pt;
            e.Handled = true;
            return;
        }

        // Node body hit?
        if (HitNode(pt, out var hitNode))
        {
            _vm.CaptureHistory("Move Field node");
            _vm.SelectedNode = hitNode;
            _mode = Mode.DragNode;
            _dragNode = hitNode;
            var world = ScreenToWorld(pt);
            _dragOffset = new Point(world.X - hitNode!.X, world.Y - hitNode.Y);
            e.Handled = true;
            return;
        }

        // Group enter (double-click empty group chrome at root)?
        if (e.ClickCount >= 2 && HitGroup(pt, out var enterGroup) && enterGroup is not null)
        {
            _vm.EnterGroup(enterGroup);
            e.Handled = true;
            return;
        }

        // Empty: pan (and clear selection).
        _vm.SelectedNode = null;
        _mode = Mode.Pan;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null) return;
        var pt = e.GetPosition(this);

        switch (_mode)
        {
            case Mode.Pan:
                PanX += pt.X - _lastPointer.X;
                PanY += pt.Y - _lastPointer.Y;
                InvalidateVisual();
                ViewChanged?.Invoke();
                break;
            case Mode.DragNode when _dragNode is not null:
                var world = ScreenToWorld(pt);
                _dragNode.X = world.X - _dragOffset.X;
                _dragNode.Y = world.Y - _dragOffset.Y;
                InvalidateVisual();
                ViewChanged?.Invoke();
                break;
            case Mode.Resize when _resizeNode is not null:
                var wr = ScreenToWorld(pt);
                _resizeNode.Width = Math.Max(90, wr.X - _resizeNode.X);
                var visualArea = wr.Y - _resizeNode.Y - PortRowsHeight(_resizeNode);
                var baseVisual = _resizeNode.HasVisual ? DefaultVisualHeight : 0;
                _resizeNode.VisualHeight = Math.Max(0, visualArea - baseVisual);
                InvalidateVisual();
                ViewChanged?.Invoke();
                break;
            case Mode.Connect:
                _connectCurrent = pt;
                InvalidateVisual();
                break;
        }

        _lastPointer = pt;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm is null) { _mode = Mode.None; return; }
        var pt = e.GetPosition(this);

        if (_mode == Mode.Connect && _connectNode is not null && _connectPort is not null)
        {
            if (HitPort(pt, out var node, out var port) && node is not null && port is not null
                && port.Direction != _connectPort.Direction && !ReferenceEquals(node, _connectNode))
            {
                _vm.CaptureHistory("Wire Field node");
                if (_connectPort.Direction == FieldPortDirection.Output)
                    _vm.Graph.Connect(_connectNode.Id, _connectPort.Id, node.Id, port.Id);
                else
                    _vm.Graph.Connect(node.Id, port.Id, _connectNode.Id, _connectPort.Id);
                _vm.NotifyStructureChanged();
            }
            else
            {
                // Dropped an input-originated rewire on empty space: the disconnect already applied.
                _vm.NotifyStructureChanged();
            }
        }
        else if (_mode == Mode.DragNode && _dragNode is not null)
        {
            _vm.FinishMoveNode(_dragNode);
        }

        _mode = Mode.None;
        _dragNode = null;
        _resizeNode = null;
        _connectNode = null;
        _connectPort = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm is null) return;
        var pt = e.GetPosition(this);
        var before = ScreenToWorld(pt);
        var factor = Math.Exp(e.Delta.Y * 0.12);
        Zoom = Math.Clamp(Zoom * factor, 0.1, 6.0);
        // Keep the point under the cursor stationary.
        PanX = pt.X - before.X * Zoom;
        PanY = pt.Y - before.Y * Zoom;
        InvalidateVisual();
        ViewChanged?.Invoke();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null) return;
        if (e.Key is Key.Delete or Key.Back && _vm.SelectedNode is not null)
        {
            _vm.RemoveSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _vm.CanExitGroup)
        {
            _vm.ExitGroup();
            e.Handled = true;
        }
    }

    private bool HitPort(Point screen, out FieldNode? node, out FieldPort? port)
    {
        node = null;
        port = null;
        if (_vm is null) return false;
        var r = (PortRadius + 5) * Math.Max(0.5, Zoom);
        foreach (var n in _vm.VisibleNodes())
        {
            for (var i = 0; i < n.Inputs.Count; i++)
            {
                var p = WorldToScreen(InputPortWorld(n, i).X, InputPortWorld(n, i).Y);
                if (Dist(p, screen) <= r) { node = n; port = n.Inputs[i]; return true; }
            }
            for (var i = 0; i < n.Outputs.Count; i++)
            {
                var p = WorldToScreen(OutputPortWorld(n, i).X, OutputPortWorld(n, i).Y);
                if (Dist(p, screen) <= r) { node = n; port = n.Outputs[i]; return true; }
            }
        }
        return false;
    }

    private bool HitNode(Point screen, out FieldNode? node)
    {
        node = null;
        if (_vm is null) return false;
        // Topmost last: iterate in reverse.
        var nodes = _vm.VisibleNodes().ToList();
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var n = nodes[i];
            var origin = WorldToScreen(n.X, n.Y);
            var rect = new Rect(origin.X, origin.Y, NodeW(n) * Zoom, NodeHeight(n) * Zoom);
            if (rect.Contains(screen)) { node = n; return true; }
        }
        return false;
    }

    private bool HitResize(Point screen, out FieldNode? node)
    {
        node = null;
        if (_vm is null) return false;
        var hs = ResizeHandle * Zoom;
        var nodes = _vm.VisibleNodes().ToList();
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var n = nodes[i];
            var origin = WorldToScreen(n.X, n.Y);
            var right = origin.X + NodeW(n) * Zoom;
            var bottom = origin.Y + NodeHeight(n) * Zoom;
            var handle = new Rect(right - hs, bottom - hs, hs, hs);
            if (handle.Contains(screen)) { node = n; return true; }
        }
        return false;
    }

    private static double Dist(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void HandleRightClick(Point pt)
    {
        if (_vm is null) return;
        if (HitNode(pt, out var node) && node is not null)
        {
            _vm.SelectedNode = node;
            var menu = new MenuFlyout();
            var del = new MenuItem { Header = Loc.Get("Menu_DeleteNode") };
            del.Click += (_, _) => _vm.RemoveNode(node);
            menu.Items.Add(del);
            if (_vm.NavigationGroupId is null && _vm.Graph.Groups.FirstOrDefault(g => g.NodeIds.Contains(node.Id)) is { } parent)
            {
                var enter = new MenuItem { Header = Loc.Format("Menu_EnterGroup", parent.Name) };
                enter.Click += (_, _) => _vm.EnterGroup(parent);
                menu.Items.Add(enter);
            }
            var disc = new MenuItem { Header = Loc.Get("Menu_DisconnectAll") };
            disc.Click += (_, _) =>
            {
                _vm.CaptureHistory("Wire Field node");
                foreach (var p in node.Inputs) _vm.Graph.DisconnectPort(node.Id, p.Id);
                foreach (var p in node.Outputs) _vm.Graph.DisconnectPort(node.Id, p.Id);
                _vm.NotifyStructureChanged();
            };
            menu.Items.Add(disc);
            menu.ShowAt(this, true);
            return;
        }

        ShowAddNodeMenu(ScreenToWorld(pt));
    }

    private void ShowAddNodeMenu(Point world)
    {
        if (_vm is null) return;
        var menu = new MenuFlyout();
        foreach (var group in _vm.PaletteGroups)
        {
            var cat = new MenuItem { Header = group.Name };
            foreach (var item in group.Items)
            {
                var mi = new MenuItem { Header = item.DisplayName };
                var typeId = item.TypeId;
                mi.Click += (_, _) => _vm.AddNode(typeId, world.X, world.Y);
                cat.Items.Add(mi);
            }
            menu.Items.Add(cat);
        }
        menu.ShowAt(this, true);
    }

    /// <summary>Adds a node (from a palette double-click / drag) at the centre of the current view.</summary>
    public void AddNodeAtViewCenter(string typeId)
    {
        var world = ScreenToWorld(new Point(Bounds.Width / 2, Bounds.Height / 2));
        _vm?.AddNode(typeId, world.X, world.Y);
    }
}
