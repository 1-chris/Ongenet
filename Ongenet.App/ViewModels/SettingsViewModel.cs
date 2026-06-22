namespace Ongenet.App.ViewModels;

/// <summary>
/// Aggregates the tabs of the Settings window: Audio devices, MIDI, and Theme. Each tab binds to its
/// own existing view-model (all DI singletons), so the window is just a host.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel(AudioDevicesViewModel audio, MidiSettingsViewModel midi, ThemeEditorViewModel theme,
        LibrarySettingsViewModel library)
    {
        Audio = audio;
        Midi = midi;
        Theme = theme;
        Library = library;
    }

    public AudioDevicesViewModel Audio { get; }
    public MidiSettingsViewModel Midi { get; }
    public ThemeEditorViewModel Theme { get; }
    public LibrarySettingsViewModel Library { get; }
}
