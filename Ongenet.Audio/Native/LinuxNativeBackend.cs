using System;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// The native Linux <see cref="IAudioBackend"/>: talks to the OS audio stack directly.
/// Aggregates whichever subsystems are present — ALSA today, with PipeWire/PulseAudio/JACK scaffolded —
/// behind one device service and one output/input pair. Supported only on Linux; if no subsystem is
/// usable it simply exposes no devices (the app stays alive and silent).
/// </summary>
public sealed class LinuxNativeBackend : IAudioBackend
{
    private readonly NativeDriverRegistry _drivers;
    private readonly NativeAudioDeviceService _devices;
    private readonly NativeAudioOutput _output;
    private readonly NativeAudioInput _input;

    public LinuxNativeBackend()
    {
        _drivers = new NativeDriverRegistry();
        _devices = new NativeAudioDeviceService(_drivers);
        _output = new NativeAudioOutput(_drivers, _devices);
        _input = new NativeAudioInput(_drivers, _devices);
    }

    public string Id => "native";
    public string DisplayName => "Native (Linux)";
    public bool IsSupported => OperatingSystem.IsLinux();

    public IAudioDeviceService Devices => _devices;
    public IAudioOutput Output => _output;
    public IAudioInput Input => _input;

    public void Dispose()
    {
        _output.Dispose();
        _input.Dispose();
    }
}
