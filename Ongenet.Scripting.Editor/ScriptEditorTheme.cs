using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Ongenet.Scripting.Editor;

/// <summary>Theme-aware syntax colours for the script editor overlay.</summary>
public static class ScriptEditorTheme
{
    /// <summary>Raised when the app palette changes; editor controls refresh highlights.</summary>
    public static event Action? ThemeChanged;

    public static void NotifyThemeChanged() => ThemeChanged?.Invoke();

    public static IBrush BrushFor(ScriptHighlightKind kind) => kind switch
    {
        ScriptHighlightKind.Keyword => Keyword,
        ScriptHighlightKind.String => String,
        ScriptHighlightKind.Comment => Comment,
        ScriptHighlightKind.Type => Type,
        ScriptHighlightKind.Method => Method,
        ScriptHighlightKind.Number => Number,
        ScriptHighlightKind.Error => Error,
        ScriptHighlightKind.Warning => Warning,
        _ => Default
    };

    public static IBrush Default => Resolve("CatppuccinText", "#CDD6F4");
    public static IBrush Keyword => Resolve("CatppuccinMauve", "#CBA6F7");
    public static IBrush String => Resolve("CatppuccinGreen", "#A6E3A1");
    public static IBrush Comment => Resolve("CatppuccinOverlay0", "#6C7086");
    public static IBrush Type => Resolve("CatppuccinBlue", "#89B4FA");
    public static IBrush Method => Resolve("CatppuccinSky", "#89DCEB");
    public static IBrush Number => Resolve("CatppuccinPeach", "#FAB387");
    public static IBrush Error => Resolve("CatppuccinRed", "#F38BA8");
    public static IBrush Warning => Resolve("CatppuccinYellow", "#F9E2AF");
    public static IBrush Caret => Resolve("CatppuccinRosewater", "#F5E0DC");
    public static IBrush Gutter => Resolve("CatppuccinOverlay1", "#585B70");
    public static IBrush EditorBackground => Resolve("CatppuccinBase", "#1E1E2E");
    public static IBrush SelectionFill => Resolve("CatppuccinSurface2", "#45475A", 0.55);
    public static IBrush PopupBackground => Resolve("CatppuccinSurface0", "#313244");
    public static IBrush PopupBorder => Resolve("CatppuccinSurface1", "#45475A");
    public static IBrush PopupForeground => Resolve("CatppuccinText", "#CDD6F4");

    private static IBrush Resolve(string resourceKey, string fallbackHex, double opacity = 1.0)
    {
        var app = Application.Current;
        if (app is not null && app.TryGetResource(resourceKey, null, out var res) && res is IBrush brush)
            return opacity < 1.0 ? WithOpacity(brush, opacity) : brush;

        var color = Color.Parse(fallbackHex);
        if (opacity < 1.0)
            color = Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B);
        return new SolidColorBrush(color);
    }

    private static IBrush WithOpacity(IBrush brush, double opacity)
    {
        if (brush is SolidColorBrush solid)
        {
            var c = solid.Color;
            return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B));
        }

        return brush;
    }
}
