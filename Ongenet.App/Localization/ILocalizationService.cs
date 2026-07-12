using System;
using System.Collections.Generic;

namespace Ongenet.App.Localization;

/// <summary>
/// Loads locale string dictionaries and applies the user's UI culture at runtime.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Setting value: follow <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>.</summary>
    const string SystemCultureId = "system";

    /// <summary>The culture id currently applied ("en", "ja", etc.).</summary>
    string CurrentCultureId { get; }

    /// <summary>Culture choices shown in Settings (id + display label).</summary>
    IReadOnlyList<LocaleOption> SupportedLocales { get; }

    /// <summary>Raised after strings are swapped and thread culture is updated.</summary>
    event Action? CultureChanged;

    /// <summary>Loads the default English dictionary. Call once at startup before the main window.</summary>
    void Initialize();

    /// <summary>Applies a culture id ("system", "en", "ja", …).</summary>
    void Apply(string cultureId);

    /// <summary>Resolves a setting id to an effective culture id (maps "system" to a supported locale).</summary>
    string ResolveCultureId(string cultureId);
}

/// <summary>A selectable locale in Settings.</summary>
public sealed record LocaleOption(string Id, string DisplayName);
