using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>Sends MIDI to external hardware or software ports.</summary>
public interface IMidiOutputService : IDisposable
{
    IReadOnlyList<MidiDeviceInfo> Devices { get; }
    MidiDeviceInfo? SelectedDevice { get; }
    bool IsAvailable { get; }

    void RefreshDevices();
    void Select(MidiDeviceInfo? device);
    void SendNote(int channel, int note, bool on, int velocity);
    void SendControlChange(int channel, int controller, int value);
    void SendRaw(byte status, byte data1 = 0, byte data2 = 0);

    event Action? DevicesChanged;
}

/// <summary>No-op when the host has no MIDI output backend.</summary>
public sealed class NullMidiOutputService : IMidiOutputService
{
    public IReadOnlyList<MidiDeviceInfo> Devices { get; } = Array.Empty<MidiDeviceInfo>();
    public MidiDeviceInfo? SelectedDevice => null;
    public bool IsAvailable => false;
    public event Action? DevicesChanged;
    public void RefreshDevices() { }
    public void Select(MidiDeviceInfo? device) { }
    public void SendNote(int channel, int note, bool on, int velocity) { }
    public void SendControlChange(int channel, int controller, int value) { }
    public void SendRaw(byte status, byte data1 = 0, byte data2 = 0) { }
    public void Dispose() { }
}
