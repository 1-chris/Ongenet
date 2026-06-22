using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;
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
    public void ApplyToServices() { }
    public void CaptureAndSave() { }
    public void SaveLibrary() => LibraryChanged?.Invoke();
    public event Action? LibraryChanged;
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
    public MidiDeviceInfo? SelectedDevice => null;
    public bool IsRunning => false;
    public void RefreshDevices() => DevicesChanged?.Invoke();
    public void Select(MidiDeviceInfo? device) => SelectedDeviceChanged?.Invoke();
    public event Action? DevicesChanged;
    public event Action? SelectedDeviceChanged;
    public event Action<MidiMessage>? MessageReceived;
    public void Dispose() { }
}
