using System;
using System.Runtime.Versioning;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native.Win;

/// <summary>
/// The Windows native <see cref="IAudioBackend"/>: talks to WASAPI directly in shared,
/// event-driven mode. Shares the id "native" with the Linux/macOS backends so the audio backend is the
/// same "Native" option on every OS; the composition root registers whichever matches the running OS.
/// Build-verified on Linux (the COM interop binds only on Windows); needs on-device shakeout.
/// </summary>
public sealed class WinNativeBackend : IAudioBackend
{
    private readonly WasapiDeviceService _devices;
    private readonly WasapiOutput _output;
    private readonly WasapiInput _input;

    [SupportedOSPlatform("windows")]
    public WinNativeBackend()
    {
        _devices = new WasapiDeviceService();
        _output = new WasapiOutput(_devices);
        _input = new WasapiInput(_devices);
    }

    public string Id => "native";
    public string DisplayName => "Native (WASAPI)";
    public bool IsSupported => OperatingSystem.IsWindows();

    public IAudioDeviceService Devices => _devices;
    public IAudioOutput Output => _output;
    public IAudioInput Input => _input;

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return; // these objects only ever exist on Windows
        _output.Dispose();
        _input.Dispose();
    }
}
