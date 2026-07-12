using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Ongenet.App.Localization;

/// <summary>Default <see cref="ILocalizationService"/> — swaps merged string dictionaries on <see cref="Application.Current"/>.</summary>
public sealed class LocalizationService : ILocalizationService
{
    private static readonly string[] SupportedCultureIds = ["en", "ja"];

    private readonly IReadOnlyList<LocaleOption> _supportedLocales =
    [
        new(ILocalizationService.SystemCultureId, "System default"),
        new("en", "English"),
        new("ja", "日本語"),
    ];

    private ResourceDictionary? _stringsDictionary;
    private string _currentCultureId = "en";

    public string CurrentCultureId => _currentCultureId;

    public IReadOnlyList<LocaleOption> SupportedLocales => _supportedLocales;

    public event Action? CultureChanged;

    public void Initialize()
    {
        Apply("en");
    }

    public string ResolveCultureId(string cultureId)
    {
        if (string.Equals(cultureId, ILocalizationService.SystemCultureId, StringComparison.OrdinalIgnoreCase))
            return ResolveSystemCultureId();

        var normalized = NormalizeCultureId(cultureId);
        return SupportedCultureIds.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : "en";
    }

    public void Apply(string cultureId)
    {
        var effective = ResolveCultureId(cultureId);
        if (_stringsDictionary is not null && string.Equals(_currentCultureId, effective, StringComparison.OrdinalIgnoreCase))
            return;

        var app = Application.Current
            ?? throw new InvalidOperationException("Application.Current is not available.");

        var uri = new Uri($"avares://Ongenet.App/Resources/Strings.{effective}.axaml");
        var loaded = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);

        if (_stringsDictionary is not null)
            app.Resources.MergedDictionaries.Remove(_stringsDictionary);

        _stringsDictionary = loaded;
        app.Resources.MergedDictionaries.Add(_stringsDictionary);

        _currentCultureId = effective;
        ApplyThreadCulture(effective);

        if (Dispatcher.UIThread.CheckAccess())
            RaiseChanged();
        else
            Dispatcher.UIThread.Post(RaiseChanged);
    }

    private void RaiseChanged()
    {
        InvalidateAllWindows();
        CultureChanged?.Invoke();
    }

    private static string ResolveSystemCultureId()
    {
        var ui = CultureInfo.CurrentUICulture;
        foreach (var part in new[] { ui.Name, ui.TwoLetterISOLanguageName })
        {
            var id = NormalizeCultureId(part);
            if (SupportedCultureIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                return id;
        }

        return "en";
    }

    private static string NormalizeCultureId(string cultureId)
    {
        if (string.IsNullOrWhiteSpace(cultureId)) return "en";
        var dash = cultureId.IndexOf('-');
        return dash > 0 ? cultureId[..dash] : cultureId;
    }

    private static void ApplyThreadCulture(string cultureId)
    {
        // WASM demo builds set InvariantGlobalization (no ICU data). Our strings come from merged
        // axaml dictionaries, so skipping thread culture there is fine — only .NET formatting APIs care.
        if (AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant) && invariant)
            return;

        var culture = CultureInfo.GetCultureInfo(cultureId);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
    }

    private static void InvalidateAllWindows()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        foreach (var window in desktop.Windows)
            window.InvalidateVisual();
    }
}
