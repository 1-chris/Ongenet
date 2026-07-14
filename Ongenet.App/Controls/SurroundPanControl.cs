using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Ongenet.App.Theming;

namespace Ongenet.App.Controls;

/// <summary>2D pan + width control for 5.1 surround placement.</summary>
public sealed class SurroundPanControl : ThemedControl
{
    public static readonly StyledProperty<double> PanProperty =
        AvaloniaProperty.Register<SurroundPanControl, double>(nameof(Pan), defaultValue: 0.0);

    public static readonly StyledProperty<double> PanWidthProperty =
        AvaloniaProperty.Register<SurroundPanControl, double>(nameof(PanWidth), defaultValue: 1.0);

    static SurroundPanControl()
    {
        AffectsRender<SurroundPanControl>(PanProperty, PanWidthProperty);
    }

    public double Pan
    {
        get => GetValue(PanProperty);
        set => SetValue(PanProperty, Math.Clamp(value, -1, 1));
    }

    public double PanWidth
    {
        get => GetValue(PanWidthProperty);
        set => SetValue(PanWidthProperty, Math.Clamp(value, 0, 1));
    }

    private IBrush _bg = Brushes.Transparent;
    private IPen _border = new Pen(Brushes.Gray);
    private IBrush _dot = Brushes.White;
    private IBrush _label = Brushes.Gray;

    protected override void BuildThemeResources()
    {
        _bg = new SolidColorBrush(ThemePalette.Surface0);
        _border = new Pen(new SolidColorBrush(ThemePalette.Surface2), 1);
        _dot = new SolidColorBrush(ThemePalette.Mauve);
        _label = new SolidColorBrush(ThemePalette.Overlay1);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        UpdateFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            UpdateFromPoint(e.GetPosition(this));
    }

    private void UpdateFromPoint(Point p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1) return;
        Pan = (p.X / w) * 2 - 1;
        PanWidth = 1 - p.Y / h;
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(_bg, _border, rect, 4);

        var labels = new[] { ("Ls", 0.15, 0.85), ("L", 0.15, 0.35), ("C", 0.5, 0.35), ("R", 0.85, 0.35), ("Rs", 0.85, 0.85) };
        foreach (var (text, nx, ny) in labels)
        {
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("fonts:Inter#Inter"), 9, _label);
            context.DrawText(ft, new Point(nx * rect.Width - ft.Width / 2, ny * rect.Height - ft.Height / 2));
        }

        var dotX = (Pan + 1) * 0.5 * rect.Width;
        var dotY = (1 - PanWidth) * rect.Height;
        context.DrawEllipse(_dot, null, new Point(dotX, dotY), 6, 6);
    }
}
