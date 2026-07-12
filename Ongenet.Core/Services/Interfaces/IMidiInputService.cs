using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Manages external MIDI controller input: device enumeration, multi-port selection, and routing of
/// incoming messages to the live-preview path (notes/CC/pitch-bend/aftertouch on the selected instrument)
/// and, in later phases, to mapped parameters and the transport. The concrete implementation lives in the
/// desktop host because it drives the platform MIDI backend (ALSA/winmm/CoreMIDI).
/// </summary>
public interface IMidiInputService : IDisposable
{
    /// <summary>The MIDI input ports currently available, refreshed by <see cref="RefreshDevices"/>.</summary>
    IReadOnlyList<MidiDeviceInfo> Devices { get; }

    /// <summary>The input devices currently open and delivering messages.</summary>
    IReadOnlyList<MidiDeviceInfo> EnabledDevices { get; }

    /// <summary>Whether at least one device is open and delivering messages.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// When false, note/CC/pitch-bend/aftertouch are not routed to the selected instrument preview path.
    /// Transport, session, and parameter mappings still apply.
    /// </summary>
    bool InstrumentInputEnabled { get; set; }

    /// <summary>Re-enumerates available input ports and raises <see cref="DevicesChanged"/>.</summary>
    void RefreshDevices();

    /// <summary>Opens the given devices for input (closing any not in the list).</summary>
    void SetEnabledDevices(IReadOnlyList<MidiDeviceInfo> devices);

    /// <summary>Raised when the available device list changes.</summary>
    event Action? DevicesChanged;

    /// <summary>Raised when <see cref="EnabledDevices"/> changes.</summary>
    event Action? EnabledDevicesChanged;

    /// <summary>Raised for every received message (e.g. to drive an input-activity indicator).</summary>
    event Action<MidiMessage>? MessageReceived;
}
