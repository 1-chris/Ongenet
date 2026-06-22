using System;
using System.Collections.Generic;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// JACK driver (libjack) for the native backend. JACK has a single server (not a list of devices), so
/// this exposes one output and one input "device" representing the server; selecting it registers ports
/// and auto-connects them to the system's physical ports via <see cref="JackStream"/>. Only surfaces
/// when libjack is loadable and a server is reachable.
/// </summary>
internal sealed class JackAudioDriver : INativeAudioDriver
{
    public string HostApi => "JACK";
    public string IdPrefix => "jack:";

    private bool? _available;
    public bool IsAvailable => _available ??= JackNative.TryProbe();

    public void Enumerate(List<AudioDevice> outputs, List<AudioDevice> inputs)
    {
        if (!IsAvailable) return;

        // Probe for a running server by opening a transient client; skip entirely if none is up.
        var client = JackNative.jack_client_open("Ongenet-probe", JackNative.JackNullOption, out _);
        if (client == IntPtr.Zero) return;
        JackNative.jack_client_close(client);

        outputs.Add(new AudioDevice(0, "JACK server", HostApi, 0, 2, false, false, IdPrefix + "system"));
        inputs.Add(new AudioDevice(0, "JACK server", HostApi, 2, 0, false, false, IdPrefix + "system"));
    }

    public INativeStream OpenOutput(AudioDevice device, int channels, AudioRenderCallback render)
        => JackStream.Open("Ongenet", playback: true, channels, render, null);

    public INativeStream OpenInput(AudioDevice device, int channels, AudioCaptureCallback capture)
        => JackStream.Open("Ongenet", playback: false, channels, null, capture);
}
