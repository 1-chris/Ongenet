using System;
using Avalonia.Media;

namespace Ongenet.App.Theming;

/// <summary>Picks a high-contrast foreground colour for text on an accent background.</summary>
public static class ContrastForeground
{
    private static readonly Color DarkText = Color.FromRgb(17, 17, 27);
    private static readonly Color LightText = Color.FromRgb(250, 250, 255);

    public static IBrush BrushForColorKey(string? colorKey) => new SolidColorBrush(ColorForColorKey(colorKey));

    public static Color ColorForColorKey(string? colorKey) => ChooseForeground(ResolveBackground(colorKey));

    public static Color ChooseForeground(Color background)
    {
        var lum = RelativeLuminance(background);
        return lum > 0.4 ? DarkText : LightText;
    }

    private static Color ResolveBackground(string? colorKey)
    {
        if (string.IsNullOrWhiteSpace(colorKey))
            return ThemePalette.Surface1;

        const string prefix = "Catppuccin";
        if (colorKey.StartsWith(prefix, StringComparison.Ordinal) && colorKey.Length > prefix.Length)
            return ThemePalette.ColorOf(colorKey[prefix.Length..]);

        try
        {
            return Color.Parse(colorKey);
        }
        catch (FormatException)
        {
            return ThemePalette.Surface1;
        }
    }

    /// <summary>WCAG 2.x relative luminance for sRGB.</summary>
    public static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
