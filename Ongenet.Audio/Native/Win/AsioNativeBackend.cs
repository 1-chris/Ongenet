using System;
using System.Runtime.Versioning;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native.Win;

/// <summary>
/// Optional Windows ASIO backend skeleton. Supported on Windows; devices appear only when built with
/// <c>ENABLE_ASIO</c> and a driver is present — otherwise the app keeps using WASAPI.
/// </summary>
public sealed class AsioNativeBackend : IAudioBackend
{
    private readonly AsioDeviceService _devices;
    private readonly AsioOutput _output;
    private readonly AsioInput _input;

    [SupportedOSPlatform("windows")]
    public AsioNativeBackend()
    {
        _devices = new AsioDeviceService();
        _output = new AsioOutput(_devices);
        _input = new AsioInput(_devices);
    }

    public string Id => "asio";
    public string DisplayName => "ASIO (experimental)";
    public bool IsSupported => OperatingSystem.IsWindows();

    public IAudioDeviceService Devices => _devices;
    public IAudioOutput Output => _output;
    public IAudioInput Input => _input;

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        _output.Dispose();
        _input.Dispose();
    }
}
