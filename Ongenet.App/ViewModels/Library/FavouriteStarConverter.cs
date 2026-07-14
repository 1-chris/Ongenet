using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ongenet.App.ViewModels.Library;

/// <summary>Maps <c>IsFavourite</c> to filled vs outline star geometry from app resources.</summary>
public sealed class FavouriteStarConverter : IValueConverter
{
    public static readonly FavouriteStarConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var filled = value is true;
        var key = filled ? "IconStar" : "IconStarOutline";
        var app = Application.Current;
        if (app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var res) && res is Geometry g)
            return g;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
