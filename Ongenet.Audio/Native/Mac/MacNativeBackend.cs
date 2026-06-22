using System;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native.Mac;

/// <summary>
/// The macOS native <see cref="IAudioBackend"/>: talks to CoreAudio directly via the HAL
/// output AudioUnit. Shares the id "native" with the Linux backend, so the audio backend is the same
/// "Native" option on every OS; the composition root registers whichever one matches the running OS.
/// Build-verified on Linux (the CoreAudio frameworks bind lazily and are only ever touched on macOS).
/// </summary>
public sealed class MacNativeBackend : IAudioBackend
{
    private readonly MacAudioDeviceService _devices;
    private readonly MacAudioOutput _output;
    private readonly MacAudioInput _input;

    public MacNativeBackend()
    {
        _devices = new MacAudioDeviceService();
        _output = new MacAudioOutput(_devices);
        _input = new MacAudioInput(_devices);
    }

    public string Id => "native";
    public string DisplayName => "Native (CoreAudio)";
    public bool IsSupported => OperatingSystem.IsMacOS();

    public IAudioDeviceService Devices => _devices;
    public IAudioOutput Output => _output;
    public IAudioInput Input => _input;

    public void Dispose()
    {
        _output.Dispose();
        _input.Dispose();
    }
}
