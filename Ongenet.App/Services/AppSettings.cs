using System.Collections.Generic;
using Ongenet.App.Localization;

namespace Ongenet.App.Services;

/// <summary>
/// Serializable app-wide preferences persisted to the per-user config file. Audio/MIDI devices are
/// stored by display name (stable across reconnects, unlike a backend index); the theme by name + variant.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Which low-level audio backend to use, e.g. "native". Empty = unset → use the default chosen by
    /// <see cref="Core.Audio.AudioBackendManager"/> (the OS-native backend).
    /// </summary>
    public string AudioBackend { get; set; } = "";
    public string? AudioOutputDevice { get; set; }
    public string? AudioInputDevice { get; set; }
    public string InputChannelMode { get; set; } = "Stereo";
    public string? MidiInputDevice { get; set; }

    /// <summary>Display names of enabled MIDI input ports (multi-select).</summary>
    public List<string> MidiInputDevices { get; set; } = new();

    /// <summary>
    /// When set, controls whether MIDI notes/CC reach the selected instrument. Null = auto (off in
    /// Session/Hybrid, on in Arrangement).
    /// </summary>
    public bool? MidiInstrumentInputEnabled { get; set; }

    /// <summary>When true, pending session captures are committed when transport stops.</summary>
    public bool CommitSessionCaptureOnStop { get; set; }
    public bool MidiClockEnabled { get; set; }
    public string? ThemeName { get; set; }
    public bool ThemeIsLight { get; set; }
    public double InputQuantizeBeats { get; set; }
    public List<TransportMappingDto> TransportMappings { get; set; } = new();

    /// <summary>Folders scanned for the Samples library tab.</summary>
    public List<string> SampleScanPaths { get; set; } = new();

    /// <summary>Folders scanned for the Soundfonts library tab (.sf2/.sfz).</summary>
    public List<string> SoundFontScanPaths { get; set; } = new();

    /// <summary>Whether selecting a file in the library/files browser auto-plays it.</summary>
    public bool LibraryAutoPlay { get; set; } = true;

    /// <summary>Whether dragging an audio clip into the timeline auto-stretches it to the project tempo.</summary>
    public bool AutoStretchToTempo { get; set; } = true;

    /// <summary>Whether auto-stretch preserves pitch (time-stretch) instead of resampling (pitch tracks tempo).</summary>
    public bool AutoStretchPitchCorrection { get; set; }

    /// <summary>Recently opened/saved project files, most recent first (drives the library's Projects tab).</summary>
    public List<string> RecentProjects { get; set; } = new();

    /// <summary>Control surface profile name; null/empty = legacy MCU + Launchpad combined mapping.</summary>
    public string? ControlSurfaceProfile { get; set; }

    /// <summary>Active control-surface definition id (<c>.ongencontroller</c>); preferred over legacy profile.</summary>
    public string? ControlSurfaceDefinitionId { get; set; }

    /// <summary>When true, Windows WASAPI uses exclusive mode for lower latency.</summary>
    public bool WasapiExclusiveMode { get; set; }

    /// <summary>Saved window layout profiles (multi-monitor workspace).</summary>
    public List<WindowLayoutProfileDto> WindowLayouts { get; set; } = new();

    /// <summary>Name of the active window layout profile.</summary>
    public string? ActiveWindowLayout { get; set; }

    /// <summary>Folder path for optional project collaboration sync.</summary>
    public string? CollaborationSyncFolder { get; set; }

    /// <summary>When true, periodically save autosave backups while editing.</summary>
    public bool AutosaveEnabled { get; set; } = true;

    /// <summary>Minutes between autosave writes (default 5).</summary>
    public int AutosaveIntervalMinutes { get; set; } = 5;

    /// <summary>Learned/custom mixer CC mappings for control surfaces.</summary>
    public List<ControlSurfaceMappingDto> ControlSurfaceMappings { get; set; } = new();

    /// <summary>Custom keyboard shortcut overrides.</summary>
    public List<KeyboardShortcutDto> KeyboardShortcuts { get; set; } = new();

    /// <summary>UI culture: "system", "en", "ja", etc.</summary>
    public string UiCulture { get; set; } = ILocalizationService.SystemCultureId;

    /// <summary>When true, VST3 plugins run in an isolated child process (desktop only).</summary>
    public bool PluginIsolationEnabled { get; set; }

    /// <summary>When true, waveforms draw bass/mid/treble layers in theme colours.</summary>
    public bool WaveformBandColorsEnabled { get; set; } = true;
}

public sealed class KeyboardShortcutDto
{
    public string Action { get; set; } = "";
    public string Key { get; set; } = "";
    public string Modifiers { get; set; } = "";
}

public sealed class ControlSurfaceMappingDto
{
    public string Profile { get; set; } = "";
    public int MixerChannel { get; set; }
    public int CcNumber { get; set; }
    public string Target { get; set; } = "Volume";
}

public sealed class WindowLayoutProfileDto
{
    public string Name { get; set; } = "";
    public double MainWindowX { get; set; }
    public double MainWindowY { get; set; }
    public double MainWindowWidth { get; set; }
    public double MainWindowHeight { get; set; }
    public bool MainWindowMaximized { get; set; }
}

/// <summary>Serializable form of a <see cref="Core.Audio.Midi.TransportMapping"/>.</summary>
public sealed class TransportMappingDto
{
    public string Action { get; set; } = "";
    public bool IsNote { get; set; }
    public int Channel { get; set; } = -1;
    public int Number { get; set; }
}
