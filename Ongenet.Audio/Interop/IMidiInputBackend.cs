using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Platform MIDI-input backend: enumerates input ports and delivers parsed
/// <see cref="MidiMessage"/>s on dedicated background threads.
/// </summary>
public interface IMidiInputBackend : IDisposable
{
    IReadOnlyList<MidiDeviceInfo> EnumerateDevices();

    /// <summary>Subscribes to <paramref name="device"/>; messages are tagged with its <see cref="MidiDeviceInfo.OpenId"/>.</summary>
    void Connect(MidiDeviceInfo device, Action<MidiMessage> onMessage);

    void Disconnect(MidiDeviceInfo device);

    void DisconnectAll();

    IReadOnlyList<MidiDeviceInfo> ConnectedDevices { get; }

    /// <summary>Whether at least one device is connected.</summary>
    bool IsCapturing { get; }

    /// <summary>Opens a single device (disconnects all others first).</summary>
    void Start(MidiDeviceInfo device, Action<MidiMessage> onMessage)
    {
        DisconnectAll();
        Connect(device, onMessage);
    }

    /// <summary>Disconnects all devices.</summary>
    void Stop() => DisconnectAll();
}
