using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Audio.Interop;

/// <summary>
/// Platform MIDI-input backend: enumerates input ports and, while started, delivers parsed
/// <see cref="MidiMessage"/>s on a dedicated background thread. One backend per process; the
/// concrete implementation is chosen by <see cref="MidiInputBackendFactory"/> per operating system.
/// </summary>
public interface IMidiInputBackend : IDisposable
{
    /// <summary>Enumerates the available MIDI input ports.</summary>
    IReadOnlyList<MidiDeviceInfo> EnumerateDevices();

    /// <summary>
    /// Opens <paramref name="device"/> and begins delivering messages via <paramref name="onMessage"/>,
    /// invoked on the backend's own thread (not the UI or audio thread). Stops any prior capture first.
    /// </summary>
    void Start(MidiDeviceInfo device, Action<MidiMessage> onMessage);

    /// <summary>Closes the open device and stops the delivery thread.</summary>
    void Stop();

    /// <summary>Whether a device is open and the read thread is running.</summary>
    bool IsCapturing { get; }
}
