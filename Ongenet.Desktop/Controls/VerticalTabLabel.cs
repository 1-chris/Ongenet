using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Ongenet.Desktop.Controls;

/// <summary>
/// Draws a single line of text rotated 90° (for the left-hand vertical tab strip), measured tight and
/// painted exactly centred within its own bounds. Doing the rotation + centring by hand (rather than a
/// <c>LayoutTransformControl</c> inside a <c>TabItem</c> header) avoids the off-centre result that the
/// tab template's content alignment / min-width produced, and makes the result deterministic.
/// </summary>
public sealed class VerticalTabLabel : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<VerticalTabLabel, string?>(nameof(Text));

    // Inherit the ambient text colour/size so selected vs. unselected tab colours work for free.
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<VerticalTabLabel>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<VerticalTabLabel>();

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<VerticalTabLabel>();

    /// <summary>Padding added on the short (cross) axis — i.e. left/right of the finished tab.</summary>
    public static readonly StyledProperty<double> CrossPaddingProperty =
        AvaloniaProperty.Register<VerticalTabLabel, double>(nameof(CrossPadding), 0);

    static VerticalTabLabel()
    {
        AffectsMeasure<VerticalTabLabel>(TextProperty, FontSizeProperty, FontFamilyProperty, CrossPaddingProperty);
        AffectsRender<VerticalTabLabel>(TextProperty, ForegroundProperty, FontSizeProperty, FontFamilyProperty);
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double CrossPadding { get => GetValue(CrossPaddingProperty); set => SetValue(CrossPaddingProperty, value); }

    private FormattedText? Build()
    {
        var text = Text;
        if (string.IsNullOrEmpty(text)) return null;
        var size = FontSize > 0 ? FontSize : 12;
        var typeface = new Typeface(FontFamily ?? FontFamily.Default);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, Foreground ?? Brushes.Gray);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var ft = Build();
        if (ft is null) return default;
        // Rotated 90°: the text's width becomes the control's height and vice-versa.
        return new Size(ft.Height + CrossPadding * 2, ft.Width);
    }

    public override void Render(DrawingContext context)
    {
        var ft = Build();
        if (ft is null) return;

        // Centre the text box at the control's centre, rotated 90° clockwise.
        var m = Matrix.CreateTranslation(-ft.Width / 2, -ft.Height / 2)
                * Matrix.CreateRotation(Math.PI / 2)
                * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);
        using (context.PushTransform(m))
            context.DrawText(ft, new Point(0, 0));
    }
}
