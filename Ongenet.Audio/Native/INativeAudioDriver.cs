using System.Collections.Generic;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// A single Linux audio subsystem (ALSA, PipeWire, PulseAudio, JACK) behind the native backend. Each
/// driver enumerates its own devices (tagged with <see cref="HostApi"/> and an <see cref="IdPrefix"/>
/// on <see cref="AudioDevice.Id"/>) and opens float32-interleaved streams on them. <see cref="LinuxNativeBackend"/>
/// aggregates every available driver into one device list and dispatches open requests by device id.
/// </summary>
internal interface INativeAudioDriver
{
    /// <summary>Display tag shown in device names, e.g. "ALSA".</summary>
    string HostApi { get; }

    /// <summary>Prefix this driver puts on <see cref="AudioDevice.Id"/>, e.g. "alsa:". Used to route opens.</summary>
    string IdPrefix { get; }

    /// <summary>Whether the subsystem's native library is present and usable on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Appends this driver's playback/capture devices to the given lists.</summary>
    void Enumerate(List<AudioDevice> outputs, List<AudioDevice> inputs);

    /// <summary>
    /// Opens and starts a playback stream on <paramref name="device"/> that pulls blocks from
    /// <paramref name="render"/> on its own audio thread. <paramref name="channels"/> is the desired
    /// channel count (the driver may negotiate down). Throws if the device cannot be opened.
    /// </summary>
    INativeStream OpenOutput(AudioDevice device, int channels, AudioRenderCallback render);

    /// <summary>
    /// Opens and starts a capture stream on <paramref name="device"/> that pushes blocks to
    /// <paramref name="capture"/> on its own audio thread. Throws if the device cannot be opened.
    /// </summary>
    INativeStream OpenInput(AudioDevice device, int channels, AudioCaptureCallback capture);
}
