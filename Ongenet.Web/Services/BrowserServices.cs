using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Display;
using Ongenet.App.Services;

namespace Ongenet.Web.Services;

/// <summary>
/// Browser-safe <see cref="IAppSettingsService"/>: there is no per-user config file in the sandbox, so
/// settings live only in memory for the session. Applying/saving are no-ops.
/// </summary>
public sealed class BrowserAppSettingsService : IAppSettingsService
{
    public AppSettings Current { get; } = new();
    public string FilePath => string.Empty;
    public void ApplyToServices() => WaveformDisplayPreferences.Apply(Current.WaveformBandColorsEnabled);
    public void CaptureAndSave() { }
    public void SetUiCulture(string cultureId) => Current.UiCulture = cultureId;
    public void SetPluginIsolationEnabled(bool enabled) => Current.PluginIsolationEnabled = enabled;
    public void SetWaveformBandColorsEnabled(bool enabled)
    {
        Current.WaveformBandColorsEnabled = enabled;
        WaveformDisplayPreferences.Apply(enabled);
    }

    public void SetVideoEnabled(bool enabled)
    {
        Current.VideoEnabled = enabled;
        VideoEnabledChanged?.Invoke();
    }

    public void SaveLibrary() => LibraryChanged?.Invoke();
    public event Action? LibraryChanged;
    public event Action? VideoEnabledChanged;
}

/// <summary>
/// Browser-safe <see cref="ILibraryScanService"/>: the browser cannot enumerate a real filesystem, so the
/// sample/soundfont library tabs are empty. (Built-in instruments and effects still appear — they come
/// from the in-process registries, not the disk.) A future version could index uploads into OPFS.
/// </summary>
public sealed class BrowserLibraryScanService : ILibraryScanService
{
    public IReadOnlyList<LibraryGroup> Samples { get; } = Array.Empty<LibraryGroup>();
    public IReadOnlyList<LibraryGroup> SoundFonts { get; } = Array.Empty<LibraryGroup>();
    public event Action? Changed;
    public void Rescan() => Changed?.Invoke();
}

/// <summary>
/// Browser-safe <see cref="IPresetLibrary"/>: no preset files on disk, and saving is unavailable in the
/// demo. The instrument/effect-preset tabs are therefore empty.
/// </summary>
public sealed class BrowserPresetLibrary : IPresetLibrary
{
    public IReadOnlyList<PresetGroup> InstrumentPresets { get; } = Array.Empty<PresetGroup>();
    public IReadOnlyList<PresetGroup> EffectPresets { get; } = Array.Empty<PresetGroup>();
    public IReadOnlyList<PresetGroup> ChainPresets { get; } = Array.Empty<PresetGroup>();
    public event Action? Changed;
    public void Rescan() => Changed?.Invoke();
    public string SaveInstrument(IInstrument instrument, string name) => string.Empty;
    public string SaveFieldPatch(IInstrument fieldInstrument, string name) => string.Empty;
    public string SaveFieldEffectPatch(FieldEffect fieldEffect, string name) => string.Empty;
    public string SaveEffect(IAudioEffect effect, string name) => string.Empty;
    public string SaveChain(IReadOnlyList<IAudioEffect> effects, string name) => string.Empty;
}

/// <summary>
/// Placeholder <see cref="IMidiInputService"/> for the browser. Reports no devices. The Web MIDI API
/// (<c>navigator.requestMIDIAccess</c>) is the eventual replacement for live controller input.
/// </summary>
public sealed class BrowserMidiInputService : IMidiInputService
{
    public IReadOnlyList<MidiDeviceInfo> Devices { get; } = Array.Empty<MidiDeviceInfo>();
    public IReadOnlyList<MidiDeviceInfo> EnabledDevices { get; } = Array.Empty<MidiDeviceInfo>();
    public bool IsRunning => false;
    public bool InstrumentInputEnabled { get; set; } = true;
    public void RefreshDevices() => DevicesChanged?.Invoke();
    public void SetEnabledDevices(IReadOnlyList<MidiDeviceInfo> devices) => EnabledDevicesChanged?.Invoke();
    public event Action? DevicesChanged;
    public event Action? EnabledDevicesChanged;
    public event Action<MidiMessage>? MessageReceived { add { } remove { } }
    public void Dispose() { }
}
