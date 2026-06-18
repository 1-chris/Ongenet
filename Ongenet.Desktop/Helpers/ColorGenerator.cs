using Avalonia.Media;
using Ongenet.Core.Utils;

namespace Ongenet.Desktop.Helpers
{
    /// <summary>
    /// Provides methods for generating deterministic colors based on strings.
    /// Delegates to Ongenet.Core.Utils.ColorUtils.
    /// </summary>
    public static class ColorGenerator
    {
        /// <summary>
        /// Generates a deterministic color for a given name.
        /// </summary>
        /// <param name="name">The name to generate a color for.</param>
        /// <returns>A <see cref="Color"/> generated from the name's hash.</returns>
        public static Color GetColorForName(string name)
        {
            // Delegate to Core utility
            var rgb = ColorUtils.GetColorFromString(name, saturation: 0.8, lightness: 0.7);
            return Color.FromRgb(rgb.R, rgb.G, rgb.B);
        }
    }
}
