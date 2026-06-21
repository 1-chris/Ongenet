using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Desktop.Services;

namespace Ongenet.Desktop.ViewModels;

/// <summary>
/// Settings tab for the library: the folders scanned for samples and sound fonts, and the auto-play
/// toggle. Edits are written through <see cref="IAppSettingsService"/> (which persists and triggers a
/// rescan).
/// </summary>
public sealed class LibrarySettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settings;
    private string? _selectedSample;
    private string? _selectedSoundFont;

    public LibrarySettingsViewModel(IAppSettingsService settings)
    {
        _settings = settings;
        SamplePaths = new ObservableCollection<string>(settings.Current.SampleScanPaths);
        SoundFontPaths = new ObservableCollection<string>(settings.Current.SoundFontScanPaths);
    }

    public ObservableCollection<string> SamplePaths { get; }
    public ObservableCollection<string> SoundFontPaths { get; }

    public string? SelectedSamplePath
    {
        get => _selectedSample;
        set => SetField(ref _selectedSample, value);
    }

    public string? SelectedSoundFontPath
    {
        get => _selectedSoundFont;
        set => SetField(ref _selectedSoundFont, value);
    }

    public bool AutoPlay
    {
        get => _settings.Current.LibraryAutoPlay;
        set
        {
            if (_settings.Current.LibraryAutoPlay == value) return;
            _settings.Current.LibraryAutoPlay = value;
            _settings.SaveLibrary();
            OnPropertyChanged();
        }
    }

    public void AddSampleFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || SamplePaths.Contains(path)) return;
        SamplePaths.Add(path);
        Persist();
    }

    public void RemoveSelectedSample()
    {
        if (_selectedSample is not null && SamplePaths.Remove(_selectedSample)) Persist();
    }

    public void AddSoundFontFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || SoundFontPaths.Contains(path)) return;
        SoundFontPaths.Add(path);
        Persist();
    }

    public void RemoveSelectedSoundFont()
    {
        if (_selectedSoundFont is not null && SoundFontPaths.Remove(_selectedSoundFont)) Persist();
    }

    private void Persist()
    {
        _settings.Current.SampleScanPaths = SamplePaths.ToList();
        _settings.Current.SoundFontScanPaths = SoundFontPaths.ToList();
        _settings.SaveLibrary();
    }
}
