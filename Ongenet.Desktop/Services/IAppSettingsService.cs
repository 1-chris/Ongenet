namespace Ongenet.Desktop.Services;

/// <summary>
/// Loads and persists app-wide preferences (audio/MIDI device selection, theme, input quantize,
/// transport mappings) to the per-user config file, applies them to the live services at startup, and
/// re-captures + saves them when they change.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>The loaded settings (mutated as the user changes things).</summary>
    AppSettings Current { get; }

    /// <summary>Absolute path of the settings file on disk.</summary>
    string FilePath { get; }

    /// <summary>Applies the loaded settings to the live services. Call once at startup.</summary>
    void ApplyToServices();

    /// <summary>Reads the current service state into <see cref="Current"/> and writes it to disk.</summary>
    void CaptureAndSave();
}
