using System;
using System.Collections.Generic;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.Services;

/// <summary>
/// Default <see cref="IMidiInputService"/>. Drives the platform MIDI backend (ALSA/winmm/CoreMIDI) and
/// routes incoming messages. Transport-mapped controls and learn get first refusal; then CC parameter
/// mappings; otherwise notes/CC/pitch-bend/aftertouch act on the selected track's instrument via
/// <see cref="IPreviewService"/>, so hardware playing is recorded and lights the on-screen keyboard.
/// Messages arrive on the backend thread; the downstream services are thread-safe / marshal as needed.
///
/// The device selection is restored from app settings at startup; if none is saved, the first available
/// port is opened so a freshly plugged-in controller just works.
/// </summary>
public sealed class MidiInputService : IMidiInputService
{
    private readonly IPreviewService _preview;
    private readonly IMidiMappingService _mappings;
    private readonly ITransportMapService _transport;
    private readonly IMidiInputBackend? _backend;

    private List<MidiDeviceInfo> _devices = new();
    private MidiDeviceInfo? _selected;

    public MidiInputService(IPreviewService preview, IMidiMappingService mappings, ITransportMapService transport)
    {
        _preview = preview;
        _mappings = mappings;
        _transport = transport;
        _backend = MidiInputBackendFactory.Create();
        RefreshDevices();

        // Auto-open the first port so live playing works out of the box; app settings may reselect later.
        if (_selected is null && _devices.Count > 0) Select(_devices[0]);
    }

    public IReadOnlyList<MidiDeviceInfo> Devices => _devices;

    public MidiDeviceInfo? SelectedDevice => _selected;

    public bool IsRunning => _backend?.IsCapturing ?? false;

    public event Action? DevicesChanged;
    public event Action? SelectedDeviceChanged;
    public event Action<MidiMessage>? MessageReceived;

    public void RefreshDevices()
    {
        _devices = _backend is null ? new List<MidiDeviceInfo>() : new List<MidiDeviceInfo>(_backend.EnumerateDevices());
        DevicesChanged?.Invoke();
    }

    public void Select(MidiDeviceInfo? device)
    {
        if (_backend is null) return;

        _backend.Stop();
        _selected = device;
        if (device is not null) _backend.Start(device, OnMidi);
        SelectedDeviceChanged?.Invoke();
    }

    // Invoked on the backend's read thread. Keep it quick: route and return.
    private void OnMidi(MidiMessage m)
    {
        switch (m.Kind)
        {
            case MidiMessageKind.NoteOn:
                // A note bound to a transport control triggers it instead of sounding the instrument.
                if (!_transport.HandleMessage(m)) _preview.NoteOn(m.Note, m.Velocity);
                break;
            case MidiMessageKind.NoteOff:
                _preview.NoteOff(m.Note); // harmless if the note never sounded (e.g. a transport-mapped note)
                break;
            case MidiMessageKind.ControlChange:
                // Transport buttons first, then learn / mapped parameters; an unmapped CC falls through
                // to the instrument so mod wheel, sustain, etc. still work.
                if (_transport.HandleMessage(m)) break;
                if (!_mappings.HandleControlChange(m)) _preview.ControlChange(m.Controller, m.Value);
                break;
            case MidiMessageKind.PitchBend:
                _preview.PitchBend(m.PitchBend14);
                break;
            case MidiMessageKind.ChannelAftertouch:
                _preview.ChannelAftertouch(m.Pressure);
                break;
            // PolyAftertouch / ProgramChange: not routed yet.
        }

        MessageReceived?.Invoke(m);
    }

    public void Dispose() => _backend?.Dispose();
}
