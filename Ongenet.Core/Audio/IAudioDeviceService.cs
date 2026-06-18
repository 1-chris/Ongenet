using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio;

/// <summary>
/// Enumerates the machine's audio devices and holds the user's input/output selection. The concrete
/// implementation (PortAudio) lives in Ongenet.Audio; the output and input streams read the current
/// selection from here, so picking a device in the UI reopens the affected stream on it.
/// </summary>
public interface IAudioDeviceService
{
    /// <summary>Devices that can capture audio (input channels &gt; 0).</summary>
    IReadOnlyList<AudioDevice> InputDevices { get; }

    /// <summary>Devices that can play audio (output channels &gt; 0).</summary>
    IReadOnlyList<AudioDevice> OutputDevices { get; }

    /// <summary>The chosen output device, or null to use the system default.</summary>
    AudioDevice? SelectedOutput { get; set; }

    /// <summary>The chosen input device, or null to use the system default.</summary>
    AudioDevice? SelectedInput { get; set; }

    /// <summary>How the input is captured: stereo (as-is) or mono (one channel, centered).</summary>
    AudioInputChannelMode InputChannelMode { get; set; }

    /// <summary>Re-enumerates devices (e.g. after a device is plugged in). Raises <see cref="DevicesChanged"/>.</summary>
    void Refresh();

    /// <summary>Raised when the device lists change.</summary>
    event Action? DevicesChanged;

    /// <summary>Raised when <see cref="SelectedOutput"/> changes (the output stream must reopen).</summary>
    event Action? OutputChanged;

    /// <summary>Raised when <see cref="SelectedInput"/> changes (a capture stream must reopen).</summary>
    event Action? InputChanged;
}
