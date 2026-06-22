using Ongenet.App.Services;

namespace Ongenet.App.ViewModels;

/// <summary>
/// Options shown at the bottom of the sample-oriented library tabs (Everything / Files / Samples):
/// whether dragging an audio clip into the timeline auto-stretches it to the project tempo, and whether
/// that stretch preserves pitch. Backed by <see cref="IAppSettingsService"/> and persisted on change,
/// mirroring <see cref="AudioPreviewViewModel.AutoPlay"/>.
/// </summary>
public sealed class LibraryOptionsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settings;

    public LibraryOptionsViewModel(IAppSettingsService settings) => _settings = settings;

    /// <summary>Auto-stretch dropped audio loops to the project BPM.</summary>
    public bool AutoStretch
    {
        get => _settings.Current.AutoStretchToTempo;
        set
        {
            if (_settings.Current.AutoStretchToTempo == value) return;
            _settings.Current.AutoStretchToTempo = value;
            _settings.SaveLibrary();
            OnPropertyChanged();
        }
    }

    /// <summary>Preserve pitch while auto-stretching (time-stretch) instead of resampling.</summary>
    public bool PitchCorrection
    {
        get => _settings.Current.AutoStretchPitchCorrection;
        set
        {
            if (_settings.Current.AutoStretchPitchCorrection == value) return;
            _settings.Current.AutoStretchPitchCorrection = value;
            _settings.SaveLibrary();
            OnPropertyChanged();
        }
    }
}
