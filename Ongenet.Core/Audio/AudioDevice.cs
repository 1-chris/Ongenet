namespace Ongenet.Core.Audio;

/// <summary>
/// A selectable audio device reported by the backend. <see cref="Index"/> is a backend's integer
/// device handle; <see cref="Id"/> is a backend-opaque string handle
/// (e.g. an ALSA PCM name like <c>"alsa:hw:0,0"</c>, a PulseAudio sink name, a PipeWire node id) for
/// backends that don't key devices by integer. A backend uses whichever of the two it needs to open
/// the device; the rest of the fields are for display and capability filtering.
/// </summary>
public sealed record AudioDevice(
    int Index,
    string Name,
    string HostApi,
    int MaxInputChannels,
    int MaxOutputChannels,
    bool IsDefaultInput,
    bool IsDefaultOutput,
    string Id = "")
{
    /// <summary>True when this device can capture (has input channels).</summary>
    public bool SupportsInput => MaxInputChannels > 0;

    /// <summary>True when this device can play back (has output channels).</summary>
    public bool SupportsOutput => MaxOutputChannels > 0;

    /// <summary>Label shown in device pickers — name plus the host API that exposes it.</summary>
    public string DisplayName => string.IsNullOrEmpty(HostApi) ? Name : $"{Name} ({HostApi})";
}
