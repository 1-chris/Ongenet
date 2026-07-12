using System;
using System.Collections.Generic;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.Services;

/// <summary>Sends MIDI to external hardware/software ports.</summary>
public sealed class MidiOutputService : IMidiOutputService
{
    private readonly IMidiOutputBackend? _backend;
    private List<MidiDeviceInfo> _devices = new();
    private MidiDeviceInfo? _selected;

    public MidiOutputService()
    {
        _backend = MidiOutputBackendFactory.Create();
        RefreshDevices();
        if (_devices.Count > 0) Select(_devices[0]);
    }

    public IReadOnlyList<MidiDeviceInfo> Devices => _devices;
    public MidiDeviceInfo? SelectedDevice => _selected;
    public bool IsAvailable => _backend?.IsAvailable ?? false;
    public event Action? DevicesChanged;

    public void RefreshDevices()
    {
        _devices = _backend is null ? new List<MidiDeviceInfo>() : new List<MidiDeviceInfo>(_backend.EnumerateDevices());
        DevicesChanged?.Invoke();
    }

    public void Select(MidiDeviceInfo? device)
    {
        _selected = device;
        if (_backend is null) return;
        if (device is null) _backend.Close();
        else _backend.Open(device);
    }

    public void SendNote(int channel, int note, bool on, int velocity)
    {
        if (_backend is null || channel < 1 || channel > 16) return;
        var status = (on ? 0x90 : 0x80) | (channel - 1);
        _backend.SendShortMessage(status, note, Math.Clamp(velocity, 0, 127));
    }

    public void SendControlChange(int channel, int controller, int value)
    {
        if (_backend is null || channel < 1 || channel > 16) return;
        _backend.SendShortMessage(0xB0 | (channel - 1), controller, Math.Clamp(value, 0, 127));
    }

    public void SendRaw(byte status, byte data1 = 0, byte data2 = 0)
        => _backend?.SendShortMessage(status, data1, data2);

    public void Dispose() => _backend?.Dispose();
}
