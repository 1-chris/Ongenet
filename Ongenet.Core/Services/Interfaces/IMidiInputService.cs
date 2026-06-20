using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Manages external MIDI controller input: device enumeration/selection and routing of incoming
/// messages to the live-preview path (notes/CC/pitch-bend/aftertouch on the selected instrument) and,
/// in later phases, to mapped parameters and the transport. The concrete implementation lives in the
/// desktop host because it drives the platform MIDI backend (ALSA/winmm/CoreMIDI).
/// </summary>
public interface IMidiInputService : IDisposable
{
    /// <summary>The MIDI input ports currently available, refreshed by <see cref="RefreshDevices"/>.</summary>
    IReadOnlyList<MidiDeviceInfo> Devices { get; }

    /// <summary>The open input device, or null if none is selected/available.</summary>
    MidiDeviceInfo? SelectedDevice { get; }

    /// <summary>Whether a device is open and delivering messages.</summary>
    bool IsRunning { get; }

    /// <summary>Re-enumerates available input ports and raises <see cref="DevicesChanged"/>.</summary>
    void RefreshDevices();

    /// <summary>Opens <paramref name="device"/> for input (closing any previous one); null closes input.</summary>
    void Select(MidiDeviceInfo? device);

    /// <summary>Raised when the available device list changes.</summary>
    event Action? DevicesChanged;

    /// <summary>Raised when <see cref="SelectedDevice"/> changes.</summary>
    event Action? SelectedDeviceChanged;

    /// <summary>Raised for every received message (e.g. to drive an input-activity indicator).</summary>
    event Action<MidiMessage>? MessageReceived;
}
