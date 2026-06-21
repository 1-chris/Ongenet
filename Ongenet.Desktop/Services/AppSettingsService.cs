using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Styling;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Desktop.Theming;

namespace Ongenet.Desktop.Services;

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
    private readonly IMidiInputService _midi;
    private readonly IRecordingService _recording;
    private readonly ITransportMapService _transport;

    private bool _suppress;

    public AppSettingsService(IThemeService theme, IAudioDeviceService audio, IMidiInputService midi,
        IRecordingService recording, ITransportMapService transport)
    {
        _theme = theme;
        _audio = audio;
        _midi = midi;
        _recording = recording;
        _transport = transport;

        FilePath = AppPaths.SettingsFile();
        Current = Load(FilePath);

        _audio.OutputChanged += CaptureAndSave;
        _audio.InputChanged += CaptureAndSave;
        _theme.ThemeChanged += CaptureAndSave;
        _midi.SelectedDeviceChanged += CaptureAndSave;
        _transport.MappingsChanged += CaptureAndSave;
    }

    public AppSettings Current { get; private set; }

    public string FilePath { get; }

    public void ApplyToServices()
    {
        _suppress = true;
        try
        {
            ApplyTheme();
            ApplyAudio();
            ApplyMidi();
            _recording.InputQuantizeBeats = Current.InputQuantizeBeats;
            _transport.SetMappings(Current.TransportMappings.Select(ToMapping).OfType<TransportMapping>());
        }
        finally
        {
            _suppress = false;
        }
    }

    public void CaptureAndSave()
    {
        if (_suppress) return;

        Current.AudioOutputDevice = _audio.SelectedOutput?.Name;
        Current.AudioInputDevice = _audio.SelectedInput?.Name;
        Current.InputChannelMode = _audio.InputChannelMode.ToString();
        Current.MidiInputDevice = _midi.SelectedDevice?.DisplayName;
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
    }

    private void ApplyMidi()
    {
        if (string.IsNullOrEmpty(Current.MidiInputDevice)) return;
        var dev = _midi.Devices.FirstOrDefault(x => x.DisplayName == Current.MidiInputDevice);
        if (dev is not null) _midi.Select(dev);
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
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt/unreadable settings → start fresh.
        }

        return new AppSettings();
    }
}
