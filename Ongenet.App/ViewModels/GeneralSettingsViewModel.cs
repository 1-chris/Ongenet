using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Localization;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels;

/// <summary>Settings tab for language / locale.</summary>
public sealed class GeneralSettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settings;
    private readonly ILocalizationService _localization;
    private LocaleChoice? _selectedLocale;

    public GeneralSettingsViewModel(IAppSettingsService settings, ILocalizationService localization)
    {
        _settings = settings;
        _localization = localization;
        _localization.CultureChanged += OnCultureChanged;
        RefreshChoices();
    }

    public ObservableCollection<LocaleChoice> Locales { get; } = new();

    public LocaleChoice? SelectedLocale
    {
        get => _selectedLocale;
        set
        {
            if (value is null || _selectedLocale == value) return;
            _selectedLocale = value;
            OnPropertyChanged();
            _settings.SetUiCulture(value.Id);
        }
    }

    private void RefreshChoices()
    {
        Locales.Clear();
        foreach (var opt in _localization.SupportedLocales)
        {
            var label = opt.Id switch
            {
                ILocalizationService.SystemCultureId => L("Settings_LocaleSystem"),
                "en" => L("Settings_LocaleEnglish"),
                "ja" => L("Settings_LocaleJapanese"),
                _ => opt.DisplayName
            };
            Locales.Add(new LocaleChoice(opt.Id, label));
        }

        var current = _settings.Current.UiCulture;
        _selectedLocale = Locales.FirstOrDefault(x => x.Id == current) ?? Locales.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedLocale));
    }

    private void OnCultureChanged()
    {
        RefreshChoices();
        OnPropertyChanged(nameof(RestartNote));
    }

    public string RestartNote => L("Settings_RestartNote");

    public bool PluginIsolationEnabled
    {
        get => _settings.Current.PluginIsolationEnabled;
        set
        {
            if (_settings.Current.PluginIsolationEnabled == value) return;
            _settings.SetPluginIsolationEnabled(value);
            OnPropertyChanged();
        }
    }

    public bool WaveformBandColorsEnabled
    {
        get => _settings.Current.WaveformBandColorsEnabled;
        set
        {
            if (_settings.Current.WaveformBandColorsEnabled == value) return;
            _settings.SetWaveformBandColorsEnabled(value);
            OnPropertyChanged();
        }
    }
}

public sealed record LocaleChoice(string Id, string DisplayName);
