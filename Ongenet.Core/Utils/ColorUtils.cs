using System;

namespace Ongenet.Core.Utils;

/// <summary>
/// RGB color representation.
/// </summary>
public record RgbColor(byte R, byte G, byte B);

/// <summary>
/// Utilities for generating deterministic colors from strings.
/// </summary>
public static class ColorUtils
{
    /// <summary>
    /// Generates a deterministic color for a given string (e.g., username).
    /// Uses FNV-1a-style hashing for consistent results.
    /// </summary>
    /// <param name="input">The input string to generate a color for.</param>
    /// <param name="saturation">Saturation value (0-1). Default: 0.8</param>
    /// <param name="lightness">Lightness value (0-1). Default: 0.7</param>
    /// <returns>An RGB color.</returns>
    public static RgbColor GetColorFromString(string input, double saturation = 0.8, double lightness = 0.7)
    {
        if (string.IsNullOrEmpty(input))
            return new RgbColor(255, 255, 255); // White default

        // Simple deterministic hash (FNV-1a-ish)
        uint hash = 2166136261;
        foreach (char c in input)
        {
            hash = (hash ^ c) * 16777619;
        }

        // Use the hash to pick a hue (0-360)
        double hue = hash % 360;

        return FromHsl(hue, saturation, lightness);
    }

    /// <summary>
    /// Converts HSL color values to RGB.
    /// </summary>
    /// <param name="h">The hue (0-360).</param>
    /// <param name="s">The saturation (0-1).</param>
    /// <param name="l">The lightness (0-1).</param>
    /// <returns>An RGB color.</returns>
    public static RgbColor FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2.0 - 1.0));
        double m = l - c / 2.0;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new RgbColor(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255)
        );
    }
}
