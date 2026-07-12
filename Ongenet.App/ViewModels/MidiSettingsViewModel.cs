using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Ongenet.App.Services;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Localization;

namespace Ongenet.App.ViewModels;

/// <summary>
/// Backs the MIDI tab of the Settings window: multi-device input selection, an input-activity readout,
/// the record input-quantize grid, the list of CC→parameter "MIDI learn" mappings (with removal), and the
/// transport-control mappings (play/pause, stop, record) with per-action learn/clear.
/// </summary>
public sealed class MidiSettingsViewModel : ViewModelBase
{
    private static readonly TransportAction[] TransportActions =
        { TransportAction.PlayPause, TransportAction.Stop, TransportAction.Record };

    private readonly IMidiInputService _midi;
    private readonly IMidiMappingService _mappings;
    private readonly ITransportMapService _transport;
    private readonly ISessionMidiMapService _sessionMaps;
    private readonly IRecordingService _recording;
    private readonly IAppSettingsService _settings;
    private readonly KeyboardShortcutService _shortcuts;

    public MidiSettingsViewModel(IMidiInputService midi, IMidiMappingService mappings,
        ITransportMapService transport, ISessionMidiMapService sessionMaps, IRecordingService recording,
        IAppSettingsService settings, KeyboardShortcutService shortcuts)
    {
        _midi = midi;
        _mappings = mappings;
        _transport = transport;
        _sessionMaps = sessionMaps;
        _recording = recording;
        _settings = settings;
        _shortcuts = shortcuts;

        _midi.DevicesChanged += () => Dispatcher.UIThread.Post(RefreshDeviceRows);
        _midi.EnabledDevicesChanged += () => Dispatcher.UIThread.Post(RefreshDeviceRows);
        _midi.MessageReceived += m => Dispatcher.UIThread.Post(() => Activity = Describe(m));
        _mappings.MappingsChanged += () => Dispatcher.UIThread.Post(RefreshMappings);
        _transport.MappingsChanged += () => Dispatcher.UIThread.Post(RefreshTransport);
        _transport.LearnStateChanged += () => Dispatcher.UIThread.Post(RefreshTransport);
        _sessionMaps.MappingsChanged += () => Dispatcher.UIThread.Post(RefreshSessionMappings);
        _sessionMaps.LearnStateChanged += () => Dispatcher.UIThread.Post(RefreshSessionMappings);
        _shortcuts.BindingsChanged += () => Dispatcher.UIThread.Post(RefreshShortcuts);

        QuantizeOptions = new[]
        {
            new QuantizeOption("Off", 0),
            new QuantizeOption("1/4", 1.0),
            new QuantizeOption("1/8", 0.5),
            new QuantizeOption("1/16", 0.25),
            new QuantizeOption("1/32", 0.125),
            new QuantizeOption("1/8 triplet", 1.0 / 3),
            new QuantizeOption("1/16 triplet", 0.5 / 3),
        };
        _selectedQuantize = QuantizeOptions.FirstOrDefault(q => Math.Abs(q.Beats - _recording.InputQuantizeBeats) < 1e-6)
                            ?? QuantizeOptions[0];

        DeviceRows = new ObservableCollection<MidiDeviceRow>();
        Mappings = new ObservableCollection<MidiMappingRow>();
        TransportRows = new ObservableCollection<TransportMapRow>();
        SessionMappingRows = new ObservableCollection<SessionMidiMapRow>();
        ShortcutRows = new ObservableCollection<KeyboardShortcutRow>();
        RefreshDeviceRows();
        RefreshMappings();
        RefreshTransport();
        RefreshSessionMappings();
        RefreshShortcuts();
    }

    public ObservableCollection<MidiDeviceRow> DeviceRows { get; }

    /// <summary>A short note about the platform backend's scope (shown under the device list).</summary>
    public string BackendNote => OperatingSystem.IsLinux()
        ? "ALSA: shows hardware/USB MIDI ports. Enable multiple ports for split controllers (e.g. APC Key 25 Control + Keys)."
        : "Enable multiple ports for split controllers (e.g. APC Key 25 Control + Keys).";

    private string _activity = "—";
    public string Activity
    {
        get => _activity;
        private set => SetField(ref _activity, value);
    }

    public QuantizeOption[] QuantizeOptions { get; }

    private QuantizeOption _selectedQuantize;
    public QuantizeOption SelectedQuantize
    {
        get => _selectedQuantize;
        set
        {
            if (!SetField(ref _selectedQuantize, value) || value is null) return;
            _recording.InputQuantizeBeats = value.Beats;
            _settings.CaptureAndSave();
        }
    }

    public ObservableCollection<MidiMappingRow> Mappings { get; }
    public ObservableCollection<TransportMapRow> TransportRows { get; }
    public ObservableCollection<SessionMidiMapRow> SessionMappingRows { get; }
    public ObservableCollection<KeyboardShortcutRow> ShortcutRows { get; }

    public void RefreshDevices()
    {
        _midi.RefreshDevices();
        RefreshDeviceRows();
    }

    internal void SetDeviceEnabled(MidiDeviceInfo device, bool enabled)
    {
        var current = _midi.EnabledDevices.ToList();
        if (enabled)
        {
            if (current.All(d => d.OpenId != device.OpenId))
                current.Add(device);
        }
        else
        {
            current.RemoveAll(d => d.OpenId == device.OpenId);
        }

        _midi.SetEnabledDevices(current);
        _settings.CaptureAndSave();
        RefreshDeviceRows();
    }

    public void RemoveMapping(MidiMappingRow row) => _mappings.Remove(row.Mapping);

    public void LearnTransport(TransportAction action) => _transport.BeginLearn(action);

    public void ClearTransport(TransportAction action) => _transport.ClearMapping(action);

    public void RemoveSessionMapping(SessionMidiMapRow row)
        => _sessionMaps.ClearMapping(row.Mapping.Action, row.Mapping.TrackId, row.Mapping.SceneIndex);

    public void ResetShortcut(AppShortcutAction action) => _shortcuts.ResetBinding(action);

    private void RefreshDeviceRows()
    {
        var enabledIds = _midi.EnabledDevices.Select(d => d.OpenId).ToHashSet(StringComparer.Ordinal);
        DeviceRows.Clear();
        foreach (var d in _midi.Devices)
            DeviceRows.Add(new MidiDeviceRow(d, enabledIds.Contains(d.OpenId), this));
    }

    private void RefreshMappings()
    {
        Mappings.Clear();
        foreach (var m in _mappings.Mappings) Mappings.Add(new MidiMappingRow(m));
    }

    private void RefreshTransport()
    {
        TransportRows.Clear();
        foreach (var a in TransportActions)
            TransportRows.Add(new TransportMapRow(a, _transport.MappingFor(a), _transport.LearnAction == a));
    }

    private void RefreshSessionMappings()
    {
        SessionMappingRows.Clear();
        foreach (var m in _sessionMaps.Mappings)
            SessionMappingRows.Add(new SessionMidiMapRow(m));
    }

    private void RefreshShortcuts()
    {
        ShortcutRows.Clear();
        foreach (var row in _shortcuts.AllRows())
            ShortcutRows.Add(row);
    }

    private static string Describe(MidiMessage m)
    {
        var src = string.IsNullOrEmpty(m.SourceDeviceId) ? "" : $"  [{m.SourceDeviceId}]";
        return $"{m.Kind}  ch {m.Channel + 1}  ({m.Data1}, {m.Data2}){src}";
    }
}

/// <summary>A row in the session MIDI mapping list.</summary>
public sealed class SessionMidiMapRow
{
    public SessionMidiMapRow(SessionMidiMapping mapping)
    {
        Mapping = mapping;
        var control = mapping.IsNote ? $"Note {mapping.Number}" : $"CC {mapping.Number}";
        var target = mapping.Action switch
        {
            SessionMidiAction.LaunchSlot => $"Launch slot (scene {(mapping.SceneIndex ?? 0) + 1})",
            SessionMidiAction.LaunchScene => $"Launch scene {(mapping.SceneIndex ?? 0) + 1}",
            SessionMidiAction.QueueSlot => $"Queue slot (scene {(mapping.SceneIndex ?? 0) + 1})",
            SessionMidiAction.StopSlot => $"Stop slot (scene {(mapping.SceneIndex ?? 0) + 1})",
            SessionMidiAction.StopScene => $"Stop scene {(mapping.SceneIndex ?? 0) + 1}",
            SessionMidiAction.StopAll => "Stop all",
            SessionMidiAction.GateOn => $"Gate on (scene {(mapping.SceneIndex ?? 0) + 1})",
            SessionMidiAction.GateOff => $"Gate off (scene {(mapping.SceneIndex ?? 0) + 1})",
            _ => mapping.Action.ToString()
        };
        Label = $"{target}  —  {control}";
    }

    public SessionMidiMapping Mapping { get; }
    public string Label { get; }
}

/// <summary>A row in the MIDI input device checklist.</summary>
public sealed class MidiDeviceRow : ViewModelBase
{
    private readonly MidiSettingsViewModel _owner;

    public MidiDeviceRow(MidiDeviceInfo device, bool isEnabled, MidiSettingsViewModel owner)
    {
        Device = device;
        _isEnabled = isEnabled;
        _owner = owner;
    }

    public MidiDeviceInfo Device { get; }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetField(ref _isEnabled, value)) return;
            _owner.SetDeviceEnabled(Device, value);
        }
    }
}

/// <summary>An input-quantize grid choice (label + grid size in beats; 0 = off).</summary>
public sealed record QuantizeOption(string Label, double Beats);

/// <summary>A row in the CC-mapping list.</summary>
public sealed class MidiMappingRow
{
    public MidiMappingRow(MidiMapping mapping)
    {
        Mapping = mapping;
        var name = mapping.Target?.Name ?? mapping.Binding.Kind.ToString();
        Label = $"{name}  —  CC {mapping.Controller}";
    }

    public MidiMapping Mapping { get; }
    public string Label { get; }
}

/// <summary>A row in the transport-mapping list.</summary>
public sealed class TransportMapRow
{
    public TransportMapRow(TransportAction action, TransportMapping? mapping, bool learning)
    {
        Action = action;
        ActionName = action switch
        {
            TransportAction.PlayPause => Loc.Get("TransportAction_PlayPause"),
            TransportAction.Stop => Loc.Get("TransportAction_Stop"),
            TransportAction.Record => Loc.Get("TransportAction_Record"),
            _ => action.ToString(),
        };
        Binding = mapping is null ? Loc.Get("Status_EmDash") : mapping.IsNote ? $"Note {mapping.Number}" : $"CC {mapping.Number}";
        LearnText = learning ? Loc.Get("TransportMap_Listening") : Loc.Get("TransportMap_Learn");
    }

    public TransportAction Action { get; }
    public string ActionName { get; }
    public string Binding { get; }
    public string LearnText { get; }
}
