namespace Ongenet.Core.Audio;

/// <summary>
/// A selectable audio device reported by the backend. <see cref="Index"/> is the backend's own
/// device handle (e.g. a PortAudio device index); the rest is for display and capability filtering.
/// </summary>
public sealed record AudioDevice(
    int Index,
    string Name,
    string HostApi,
    int MaxInputChannels,
    int MaxOutputChannels,
    bool IsDefaultInput,
    bool IsDefaultOutput)
{
    /// <summary>True when this device can capture (has input channels).</summary>
    public bool SupportsInput => MaxInputChannels > 0;

    /// <summary>True when this device can play back (has output channels).</summary>
    public bool SupportsOutput => MaxOutputChannels > 0;

    /// <summary>Label shown in device pickers — name plus the host API that exposes it.</summary>
    public string DisplayName => string.IsNullOrEmpty(HostApi) ? Name : $"{Name} ({HostApi})";
}
