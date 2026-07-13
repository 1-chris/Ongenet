using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

using Ongenet.App.ViewModels.Panels;

namespace Ongenet.Desktop.Services;

/// <summary>
/// Default <see cref="IMidiInputService"/>. Drives the platform MIDI backend (ALSA/winmm/CoreMIDI) and
/// routes incoming messages. Transport-mapped controls and learn get first refusal; then CC parameter
/// mappings; otherwise notes/CC/pitch-bend/aftertouch act on the selected track's instrument via
/// <see cref="IPreviewService"/>, so hardware playing is recorded and lights the on-screen keyboard.
/// Messages arrive on the backend thread; the downstream services are thread-safe / marshal as needed.
///
/// Enabled devices are restored from app settings at startup; if none are saved, all enumerated ports
/// are opened so split controllers (e.g. APC Key 25 Control + Keys) work out of the box.
/// </summary>
public sealed class MidiInputService : IMidiInputService
{
    private readonly IPreviewService _preview;
    private readonly IMidiMappingService _mappings;
    private readonly ITransportMapService _transport;
    private readonly ISessionMidiMapService _session;
    private readonly IProjectService _project;
    private readonly MidiRetrospectiveCapture _retrospective;
    private readonly VideoTrackViewModel? _video;
    private readonly IMidiInputBackend? _backend;
    private readonly MpeZoneRouter _mpeRouter;
    private readonly List<MpeRoutedAction> _mpeActions = new();
    private readonly List<MidiDeviceInfo> _enabled = new();

    private List<MidiDeviceInfo> _devices = new();

    public MidiInputService(IPreviewService preview, IMidiMappingService mappings, ITransportMapService transport,
        ISessionMidiMapService session, IProjectService project, MidiRetrospectiveCapture retrospective,
        VideoTrackViewModel? video = null)
    {
        _preview = preview;
        _mappings = mappings;
        _transport = transport;
        _session = session;
        _project = project;
        _retrospective = retrospective;
        _video = video;
        _mpeRouter = new MpeZoneRouter(project.Current.Mpe);
        _backend = MidiInputBackendFactory.Create();
        RefreshDevices();
    }

    public IReadOnlyList<MidiDeviceInfo> Devices => _devices;

    public IReadOnlyList<MidiDeviceInfo> EnabledDevices => _enabled;

    public bool IsRunning => _backend?.IsCapturing ?? false;

    public bool InstrumentInputEnabled { get; set; } = true;

    public event Action? DevicesChanged;
    public event Action? EnabledDevicesChanged;
    public event Action<MidiMessage>? MessageReceived;

    public void RefreshDevices()
    {
        _devices = _backend is null ? new List<MidiDeviceInfo>() : new List<MidiDeviceInfo>(_backend.EnumerateDevices());
        DevicesChanged?.Invoke();
    }

    public void SetEnabledDevices(IReadOnlyList<MidiDeviceInfo> devices)
    {
        if (_backend is null) return;

        _backend.DisconnectAll();
        _enabled.Clear();

        foreach (var device in devices)
        {
            var resolved = _devices.FirstOrDefault(d => d.OpenId == device.OpenId)
                           ?? _devices.FirstOrDefault(d => d.DisplayName == device.DisplayName);
            if (resolved is null) continue;

            try
            {
                _backend.Connect(resolved, OnMidi);
                _enabled.Add(resolved);
            }
            catch
            {
                // Skip ports that fail to open (unplugged, in use, etc.).
            }
        }

        EnabledDevicesChanged?.Invoke();
    }

    // Invoked on the backend's read thread. Keep it quick: route and return.
    private void OnMidi(MidiMessage m)
    {
        _retrospective.Record(m);
        if (_mpeRouter.TryRoute(m, _mpeActions))
        {
            foreach (var action in _mpeActions) ApplyMpeAction(action);
            MessageReceived?.Invoke(m);
            return;
        }

        switch (m.Kind)
        {
            case MidiMessageKind.NoteOn:
                if (!_transport.HandleMessage(m) && !_session.HandleMessage(m))
                {
                    if (InstrumentInputEnabled) _preview.NoteOn(m.Note, m.Velocity);
                }
                _video?.OnMidiNote(m.Note, true);
                break;
            case MidiMessageKind.NoteOff:
                if (!_session.HandleMessage(m) && InstrumentInputEnabled) _preview.NoteOff(m.Note);
                _video?.OnMidiNote(m.Note, false);
                break;
            case MidiMessageKind.ControlChange:
                if (_transport.HandleMessage(m)) break;
                if (_session.HandleMessage(m)) break;
                if (!_mappings.HandleControlChange(m) && InstrumentInputEnabled)
                    _preview.ControlChange(m.Controller, m.Value);
                break;
            case MidiMessageKind.PitchBend:
                if (InstrumentInputEnabled) _preview.PitchBend(m.PitchBend14);
                break;
            case MidiMessageKind.ChannelAftertouch:
                if (InstrumentInputEnabled) _preview.ChannelAftertouch(m.Pressure);
                break;
        }

        MessageReceived?.Invoke(m);
    }

    private void ApplyMpeAction(in MpeRoutedAction action)
    {
        switch (action.Kind)
        {
            case MpeRoutedActionKind.NoteOn:
                if (!_transport.HandleMessage(new MidiMessage(MidiMessageKind.NoteOn, 0, (byte)action.Note,
                        (byte)Math.Clamp((int)(action.Velocity * 127f), 0, 127))) && InstrumentInputEnabled)
                    _preview.NoteOn(action.Note, action.Velocity);
                break;
            case MpeRoutedActionKind.NoteOff:
                if (InstrumentInputEnabled) _preview.NoteOff(action.Note);
                break;
            case MpeRoutedActionKind.NotePitchBend:
                if (InstrumentInputEnabled) _preview.NotePitchBend(action.Note, action.Value);
                break;
            case MpeRoutedActionKind.NotePressure:
                if (InstrumentInputEnabled) _preview.NotePressure(action.Note, action.Value);
                break;
            case MpeRoutedActionKind.NoteTimbre:
                if (InstrumentInputEnabled) _preview.NoteTimbre(action.Note, action.Value);
                break;
        }
    }

    public void Dispose() => _backend?.Dispose();
}
