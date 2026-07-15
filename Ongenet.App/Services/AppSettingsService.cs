using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Styling;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Display;
using Ongenet.App.Localization;
using Ongenet.App.Theming;

namespace Ongenet.App.Services;

/// <summary>
/// Default <see cref="IAppSettingsService"/>. Coordinates persistence of audio/MIDI device selection,
/// theme, input quantize and transport mappings. Subscribes to the relevant service change events so any
/// change is captured and written; a suppress flag prevents the apply-on-startup pass from re-saving.
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IThemeService _theme;
    private readonly IAudioDeviceService _audio;
    private readonly IAudioBackendManager _audioBackend;
    private readonly IMidiInputService _midi;
    private readonly IRecordingService _recording;
    private readonly ITransportMapService _transport;
    private readonly ISessionCaptureService _capture;
    private readonly ILocalizationService _localization;
    private readonly IPlaybackModeService _playback;

    private bool _suppress;

    public AppSettingsService(IThemeService theme, IAudioDeviceService audio, IAudioBackendManager audioBackend,
        IMidiInputService midi, IRecordingService recording, ITransportMapService transport,
        ISessionCaptureService capture, ILocalizationService localization, IPlaybackModeService playback)
    {
        _theme = theme;
        _audio = audio;
        _audioBackend = audioBackend;
        _midi = midi;
        _recording = recording;
        _transport = transport;
        _capture = capture;
        _localization = localization;
        _playback = playback;

        FilePath = AppPaths.SettingsFile();
        Current = Load(FilePath);

        _audio.OutputChanged += CaptureAndSave;
        _audio.InputChanged += CaptureAndSave;
        // Switching backend swaps the device list, so re-apply the saved device selection on the new one.
        _audioBackend.BackendChanged += OnBackendChanged;
        _theme.ThemeChanged += CaptureAndSave;
        _midi.EnabledDevicesChanged += CaptureAndSave;
        _transport.MappingsChanged += CaptureAndSave;
    }

    public AppSettings Current { get; private set; }

    public string FilePath { get; }

    public void ApplyToServices()
    {
        _suppress = true;
        try
        {
            ApplyLocalization();
            ApplyTheme();
            // Select the backend first so the device list ApplyAudio matches against is the right one.
            // Empty = unset → leave the manager's OS-aware default (Native on Linux/macOS) in place.
            if (!string.IsNullOrEmpty(Current.AudioBackend)) _audioBackend.Switch(Current.AudioBackend);
            ApplyAudio();
            ApplyCoreAudioLead();
            ApplyMidi();
            ApplyMidiInstrumentInput();
            _recording.InputQuantizeBeats = Current.InputQuantizeBeats;
            _transport.SetMappings(Current.TransportMappings.Select(ToMapping).OfType<TransportMapping>());
            _capture.CommitOnTransportStop = Current.CommitSessionCaptureOnStop;
            WaveformDisplayPreferences.Apply(Current.WaveformBandColorsEnabled);
        }
        finally
        {
            _suppress = false;
        }
    }

    public void CaptureAndSave()
    {
        if (_suppress) return;

        Current.AudioBackend = _audioBackend.ActiveId;
        Current.AudioOutputDevice = _audio.SelectedOutput?.Name;
        Current.AudioInputDevice = _audio.SelectedInput?.Name;
        Current.InputChannelMode = _audio.InputChannelMode.ToString();
        Current.WasapiExclusiveMode = _audio.LowLatencyExclusive;
        Current.MidiInputDevices = _midi.EnabledDevices.Select(d => d.DisplayName).ToList();
        Current.MidiInputDevice = Current.MidiInputDevices.Count == 1 ? Current.MidiInputDevices[0] : null;
        Current.ThemeName = _theme.Current.Name;
        Current.ThemeIsLight = _theme.Current.Variant == ThemeVariant.Light;
        Current.InputQuantizeBeats = _recording.InputQuantizeBeats;
        Current.TransportMappings = _transport.Mappings.Select(m => new TransportMappingDto
        {
            Action = m.Action.ToString(),
            IsNote = m.IsNote,
            Channel = m.Channel,
            Number = m.Number,
        }).ToList();

        Save();
    }

    // A runtime backend switch (from the settings toggle) lands here: re-point the device selection to
    // the saved device on the new backend's list, then persist the new backend id. Skipped during the
    // startup apply pass (ApplyToServices sequences the switch + ApplyAudio itself).
    private void OnBackendChanged()
    {
        if (_suppress) return;
        _suppress = true;
        try { ApplyAudio(); }
        finally { _suppress = false; }
        CaptureAndSave();
    }

    private void ApplyLocalization()
    {
        _localization.Apply(Current.UiCulture);
    }

    public void SetUiCulture(string cultureId)
    {
        Current.UiCulture = cultureId;
        _localization.Apply(cultureId);
        Save();
    }

    public void SetPluginIsolationEnabled(bool enabled)
    {
        Current.PluginIsolationEnabled = enabled;
        Save();
    }

    public void SetWaveformBandColorsEnabled(bool enabled)
    {
        Current.WaveformBandColorsEnabled = enabled;
        WaveformDisplayPreferences.Apply(enabled);
        Save();
    }

    public event Action? VideoEnabledChanged;

    public void SetVideoEnabled(bool enabled)
    {
        Current.VideoEnabled = enabled;
        Save();
        VideoEnabledChanged?.Invoke();
    }

    private void ApplyTheme()
    {
        if (string.IsNullOrEmpty(Current.ThemeName)) return;
        var def = _theme.BuiltIns.FirstOrDefault(t => t.Name == Current.ThemeName);
        if (def is null) return;
        var variant = Current.ThemeIsLight ? ThemeVariant.Light : ThemeVariant.Dark;
        _theme.Apply(new ThemeDefinition(def.Name, variant, def.Tokens));
    }

    private void ApplyAudio()
    {
        if (!string.IsNullOrEmpty(Current.AudioOutputDevice))
        {
            var d = _audio.OutputDevices.FirstOrDefault(x => x.Name == Current.AudioOutputDevice);
            if (d is not null) _audio.SelectedOutput = d;
        }

        if (!string.IsNullOrEmpty(Current.AudioInputDevice))
        {
            var d = _audio.InputDevices.FirstOrDefault(x => x.Name == Current.AudioInputDevice);
            if (d is not null) _audio.SelectedInput = d;
        }

        if (Enum.TryParse<AudioInputChannelMode>(Current.InputChannelMode, out var mode))
            _audio.InputChannelMode = mode;

        _audio.LowLatencyExclusive = Current.WasapiExclusiveMode;
    }

    private void ApplyCoreAudioLead()
    {
        var frames = Current.CoreAudioLeadFrames is 2048 or 4096
            ? Current.CoreAudioLeadFrames
            : 2048;
        Current.CoreAudioLeadFrames = frames;
        AudioRuntimeOptions.CoreAudioLeadFrames = frames;
    }

    private void ApplyMidiInstrumentInput()
    {
        _midi.InstrumentInputEnabled = Current.MidiInstrumentInputEnabled
                                       ?? (_playback.Mode == PlaybackMode.Arrangement);
    }

    /// <summary>Effective instrument-input state (explicit setting or playback-mode default).</summary>
    public bool ResolveMidiInstrumentInputEnabled()
        => Current.MidiInstrumentInputEnabled ?? (_playback.Mode == PlaybackMode.Arrangement);

    public void SetMidiInstrumentInputEnabled(bool? enabled)
    {
        Current.MidiInstrumentInputEnabled = enabled;
        _midi.InstrumentInputEnabled = enabled ?? (_playback.Mode == PlaybackMode.Arrangement);
        Save();
    }

    private void ApplyMidi()
    {
        var names = Current.MidiInputDevices;
        if (names.Count == 0 && !string.IsNullOrEmpty(Current.MidiInputDevice))
            names = new List<string> { Current.MidiInputDevice };

        if (names.Count == 0)
        {
            if (_midi.Devices.Count > 0)
                _midi.SetEnabledDevices(_midi.Devices);
            return;
        }

        var enabled = _midi.Devices.Where(d => names.Contains(d.DisplayName)).ToList();
        if (enabled.Count > 0)
            _midi.SetEnabledDevices(enabled);
    }

    private static TransportMapping? ToMapping(TransportMappingDto d)
        => Enum.TryParse<TransportAction>(d.Action, out var action)
            ? new TransportMapping { Action = action, IsNote = d.IsNote, Channel = d.Channel, Number = d.Number }
            : null;

    public event Action? LibraryChanged;

    public void SaveLibrary()
    {
        Save();
        LibraryChanged?.Invoke();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch
        {
            // Best effort — never let a settings write failure disrupt the session.
        }
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
                if (settings.MidiInputDevices.Count == 0 && !string.IsNullOrEmpty(settings.MidiInputDevice))
                    settings.MidiInputDevices.Add(settings.MidiInputDevice);
                return settings;
            }
        }
        catch
        {
            // Corrupt/unreadable settings → start fresh.
        }

        return new AppSettings();
    }
}
