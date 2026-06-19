using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace Ongenet.Desktop.Theming
{
    /// <summary>
    /// The live palette: the 26 theme tokens exposed to code (custom-drawn controls, dialogs) so nothing
    /// hardcodes colours. Each token is backed by the SAME <see cref="SolidColorBrush"/> instance held in
    /// <c>Application.Resources</c>, so when the theme service mutates a brush's <see cref="SolidColorBrush.Color"/>
    /// every XAML <c>{StaticResource}</c> reference and these accessors update together. <see cref="Changed"/>
    /// fires after a theme is applied so custom-drawn controls can invalidate.
    /// </summary>
    public static class ThemePalette
    {
        /// <summary>Token names, in palette order. The resource keys are "Catppuccin" + name.</summary>
        public static readonly IReadOnlyList<string> TokenNames = new[]
        {
            "Rosewater", "Flamingo", "Pink", "Mauve", "Red", "Maroon", "Peach", "Yellow",
            "Green", "Teal", "Sky", "Sapphire", "Blue", "Lavender",
            "Text", "Subtext1", "Subtext0", "Overlay2", "Overlay1", "Overlay0",
            "Surface2", "Surface1", "Surface0", "Base", "Mantle", "Crust"
        };

        private static readonly Dictionary<string, SolidColorBrush> Brushes = new();

        /// <summary>Registers the shared brush instance for a token (called once at startup by the service).</summary>
        public static void Register(string token, SolidColorBrush brush) => Brushes[token] = brush;

        public static IBrush BrushOf(string token) =>
            Brushes.TryGetValue(token, out var b) ? b : Avalonia.Media.Brushes.Magenta;

        public static Color ColorOf(string token) =>
            Brushes.TryGetValue(token, out var b) ? b.Color : Colors.Magenta;

        /// <summary>An opaque token colour with a custom alpha (for translucent overlays/fills).</summary>
        public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        /// <summary>Raised after a theme is applied; custom-drawn controls subscribe to repaint.</summary>
        public static event Action? Changed;

        public static void RaiseChanged() => Changed?.Invoke();

        // Named colour accessors (always the current theme's value).
        public static Color Rosewater => ColorOf("Rosewater");
        public static Color Flamingo => ColorOf("Flamingo");
        public static Color Pink => ColorOf("Pink");
        public static Color Mauve => ColorOf("Mauve");
        public static Color Red => ColorOf("Red");
        public static Color Maroon => ColorOf("Maroon");
        public static Color Peach => ColorOf("Peach");
        public static Color Yellow => ColorOf("Yellow");
        public static Color Green => ColorOf("Green");
        public static Color Teal => ColorOf("Teal");
        public static Color Sky => ColorOf("Sky");
        public static Color Sapphire => ColorOf("Sapphire");
        public static Color Blue => ColorOf("Blue");
        public static Color Lavender => ColorOf("Lavender");
        public static Color Text => ColorOf("Text");
        public static Color Subtext1 => ColorOf("Subtext1");
        public static Color Subtext0 => ColorOf("Subtext0");
        public static Color Overlay2 => ColorOf("Overlay2");
        public static Color Overlay1 => ColorOf("Overlay1");
        public static Color Overlay0 => ColorOf("Overlay0");
        public static Color Surface2 => ColorOf("Surface2");
        public static Color Surface1 => ColorOf("Surface1");
        public static Color Surface0 => ColorOf("Surface0");
        public static Color Base => ColorOf("Base");
        public static Color Mantle => ColorOf("Mantle");
        public static Color Crust => ColorOf("Crust");
    }
}
