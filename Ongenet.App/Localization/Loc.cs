using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace Ongenet.App.Localization;

/// <summary>Resolves localized strings from merged Avalonia resource dictionaries.</summary>
public static class Loc
{
    public static string Get(string key, string fallback = "")
    {
        if (Application.Current?.TryFindResource(key, out var value) == true && value is string s)
            return s;
        return fallback.Length > 0 ? fallback : key;
    }

    public static string Get(string key, string fallback, params object[] args)
    {
        var template = Get(key, fallback);
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentUICulture, template, args);
    }

    public static string Format(string key, params object[] args)
        => Get(key, key, args);
}
