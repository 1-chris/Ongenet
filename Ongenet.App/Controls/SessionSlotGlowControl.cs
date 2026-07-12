using System;
using Avalonia;
using Avalonia.Media;
using Ongenet.App.Theming;

namespace Ongenet.App.Controls;

/// <summary>Rounded play capsule with level-reactive inner/outer glow for session slots.</summary>
public sealed class SessionSlotGlowControl : ThemedControl
{
    private static readonly Geometry PlayGeometry =
        Geometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z");

    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<SessionSlotGlowControl, double>(nameof(Level));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<SessionSlotGlowControl, bool>(nameof(IsActive));

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<SessionSlotGlowControl, IBrush?>(nameof(Accent));

    static SessionSlotGlowControl()
    {
        AffectsRender<SessionSlotGlowControl>(LevelProperty, IsActiveProperty, AccentProperty);
        WidthProperty.OverrideDefaultValue<SessionSlotGlowControl>(76);
        HeightProperty.OverrideDefaultValue<SessionSlotGlowControl>(40);
    }

    public double Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, Math.Clamp(value, 0, 1));
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private IBrush _baseFill = Brushes.Transparent;
    private IPen _borderPen = new Pen(Brushes.Gray);
    private IBrush _playIcon = Brushes.White;
    private IBrush _playIconDim = Brushes.Gray;

    protected override void BuildThemeResources()
    {
        _baseFill = new SolidColorBrush(ThemePalette.Surface0);
        _borderPen = new Pen(new SolidColorBrush(ThemePalette.Surface2), 1);
        _playIcon = new SolidColorBrush(ThemePalette.Text);
        _playIconDim = new SolidColorBrush(ThemePalette.Overlay1);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 1 || h < 1) return;

        const double radius = 10;
        var rect = new Rect(0, 0, w, h);
        context.DrawRectangle(_baseFill, null, rect, radius, radius);

        var norm = IsActive ? MeterScale.Normalize(Level) : 0;
        if (norm > 0.01 && TryGetAccentColor(out var accent))
        {
            var outerAlpha = (byte)(30 + norm * 90);
            var outerPad = 1 + norm * 3;
            var outerRect = rect.Inflate(outerPad);
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(outerAlpha, accent.R, accent.G, accent.B)),
                null, outerRect, radius + 2, radius + 2);

            var innerAlpha = (byte)(50 + norm * 170);
            var fillH = Math.Max(4, h * (0.15 + norm * 0.85) - 4);
            var fillRect = new Rect(3, h - fillH - 3, w - 6, fillH);
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(innerAlpha, accent.R, accent.G, accent.B)),
                null, fillRect, 6, 6);

            if (norm > 0.08)
            {
                var ringAlpha = (byte)(20 + norm * 60);
                context.DrawRectangle(
                    null,
                    new Pen(new SolidColorBrush(Color.FromArgb(ringAlpha, accent.R, accent.G, accent.B)), 2),
                    rect.Deflate(1), radius - 1, radius - 1);
            }
        }

        context.DrawRectangle(null, _borderPen, rect, radius, radius);

        var iconSize = 14.0;
        var scale = iconSize / 24.0;
        var matrix = Matrix.CreateTranslation((w - iconSize) * 0.5 + 1, (h - iconSize) * 0.5)
                     * Matrix.CreateScale(scale, scale);
        using (context.PushTransform(matrix))
            context.DrawGeometry(IsActive ? _playIcon : _playIconDim, null, PlayGeometry);
    }

    private bool TryGetAccentColor(out Color color)
    {
        if (Accent is SolidColorBrush solid)
        {
            color = solid.Color;
            return true;
        }

        color = default;
        return false;
    }
}
