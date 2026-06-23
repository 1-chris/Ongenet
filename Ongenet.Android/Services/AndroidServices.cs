using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;

namespace Ongenet.Android.Services;

/// <summary>
/// Android-safe <see cref="IAppSettingsService"/>: the desktop config-file path doesn't map cleanly into
/// the Android sandbox, so for now settings live in memory for the session (applying/saving are no-ops).
/// Persisting preferences to the app's private storage is a follow-up.
/// </summary>
public sealed class AndroidAppSettingsService : IAppSettingsService
{
    public AppSettings Current { get; } = new();
    public string FilePath => string.Empty;
    public void ApplyToServices() { }
    public void CaptureAndSave() { }
    public void SaveLibrary() => LibraryChanged?.Invoke();
    public event Action? LibraryChanged;
}

/// <summary>
/// Android-safe <see cref="ILibraryScanService"/>: scoped storage means the app can't freely scan the
/// filesystem, so the sample/soundfont library tabs start empty. (Built-in instruments and effects still
/// appear — they come from the in-process registries.) Indexing user-picked folders is a follow-up.
/// </summary>
public sealed class AndroidLibraryScanService : ILibraryScanService
{
    public IReadOnlyList<LibraryGroup> Samples { get; } = Array.Empty<LibraryGroup>();
    public IReadOnlyList<LibraryGroup> SoundFonts { get; } = Array.Empty<LibraryGroup>();
    public event Action? Changed;
    public void Rescan() => Changed?.Invoke();
}

/// <summary>
/// Android-safe <see cref="IPresetLibrary"/>: no preset files on disk yet, so the preset tabs are empty
/// and saving is unavailable. The built-in instrument/effect registries are unaffected.
/// </summary>
public sealed class AndroidPresetLibrary : IPresetLibrary
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
/// Placeholder <see cref="IMidiInputService"/> for Android. Reports no devices. The Android MIDI API
/// (<c>android.media.midi</c>, USB/BLE MIDI) is the eventual replacement for live controller input.
/// </summary>
public sealed class AndroidMidiInputService : IMidiInputService
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
