using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ongenet.App.Converters
{
    /// <summary>
    /// Resolves a track's <c>ColorKey</c> (a Catppuccin palette key such as "CatppuccinMauve",
    /// or a "#rrggbb" hex string) to an <see cref="IBrush"/>. Keeps colour resolution in the
    /// view layer so the Core models stay free of any Avalonia dependency.
    /// </summary>
    public sealed class CoreColorToBrushConverter : IValueConverter
    {
        /// <summary>Shared instance for use as a static resource.</summary>
        public static readonly CoreColorToBrushConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string key && !string.IsNullOrWhiteSpace(key))
            {
                // Palette key resolved against the application's resource brushes.
                if (Application.Current is { } app &&
                    app.TryGetResource(key, app.ActualThemeVariant, out var resource) &&
                    resource is IBrush brush)
                {
                    return brush;
                }

                // Fall back to parsing a hex / named colour.
                try
                {
                    return new SolidColorBrush(Color.Parse(key));
                }
                catch (FormatException)
                {
                    // Unrecognised key: fall through to the default below.
                }
            }

            return Brushes.Gray;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
