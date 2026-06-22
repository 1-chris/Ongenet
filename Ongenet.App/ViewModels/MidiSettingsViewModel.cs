using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;

namespace Ongenet.App.ViewModels;

/// <summary>
/// Backs the MIDI tab of the Settings window: input-device selection, an input-activity readout, the
/// record input-quantize grid, the list of CC→parameter "MIDI learn" mappings (with removal), and the
/// transport-control mappings (play/pause, stop, record) with per-action learn/clear.
/// </summary>
public sealed class MidiSettingsViewModel : ViewModelBase
{
    private static readonly TransportAction[] TransportActions =
        { TransportAction.PlayPause, TransportAction.Stop, TransportAction.Record };

    private readonly IMidiInputService _midi;
    private readonly IMidiMappingService _mappings;
    private readonly ITransportMapService _transport;
    private readonly IRecordingService _recording;
    private readonly IAppSettingsService _settings;

    public MidiSettingsViewModel(IMidiInputService midi, IMidiMappingService mappings,
        ITransportMapService transport, IRecordingService recording, IAppSettingsService settings)
    {
        _midi = midi;
        _mappings = mappings;
        _transport = transport;
        _recording = recording;
        _settings = settings;

        _midi.DevicesChanged += () => Dispatcher.UIThread.Post(RaiseDevices);
        _midi.SelectedDeviceChanged += () => Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedDevice)));
        _midi.MessageReceived += m => Dispatcher.UIThread.Post(() => Activity = Describe(m));
        _mappings.MappingsChanged += () => Dispatcher.UIThread.Post(RefreshMappings);
        _transport.MappingsChanged += () => Dispatcher.UIThread.Post(RefreshTransport);
        _transport.LearnStateChanged += () => Dispatcher.UIThread.Post(RefreshTransport);

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

        Mappings = new ObservableCollection<MidiMappingRow>();
        TransportRows = new ObservableCollection<TransportMapRow>();
        RefreshMappings();
        RefreshTransport();
    }

    public IReadOnlyList<MidiDeviceInfo> Devices => _midi.Devices;

    public MidiDeviceInfo? SelectedDevice
    {
        get => _midi.SelectedDevice;
        set
        {
            if (value is null || Equals(value, _midi.SelectedDevice)) return;
            _midi.Select(value);
            OnPropertyChanged();
        }
    }

    /// <summary>A short note about the platform backend's scope (shown under the device picker).</summary>
    public string BackendNote => OperatingSystem.IsLinux()
        ? "ALSA: shows hardware/USB MIDI ports."
        : "";

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

    public void RefreshDevices() => _midi.RefreshDevices();

    public void RemoveMapping(MidiMappingRow row) => _mappings.Remove(row.Mapping);

    public void LearnTransport(TransportAction action) => _transport.BeginLearn(action);

    public void ClearTransport(TransportAction action) => _transport.ClearMapping(action);

    private void RaiseDevices()
    {
        OnPropertyChanged(nameof(Devices));
        OnPropertyChanged(nameof(SelectedDevice));
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

    private static string Describe(MidiMessage m) => $"{m.Kind}  ch {m.Channel + 1}  ({m.Data1}, {m.Data2})";
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
            TransportAction.PlayPause => "Play / Pause",
            TransportAction.Stop => "Stop",
            TransportAction.Record => "Record",
            _ => action.ToString(),
        };
        Binding = mapping is null ? "—" : mapping.IsNote ? $"Note {mapping.Number}" : $"CC {mapping.Number}";
        LearnText = learning ? "Listening…" : "Learn";
    }

    public TransportAction Action { get; }
    public string ActionName { get; }
    public string Binding { get; }
    public string LearnText { get; }
}
