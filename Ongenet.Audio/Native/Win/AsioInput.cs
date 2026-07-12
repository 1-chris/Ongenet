using System;
using System.Runtime.Versioning;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native.Win;

/// <summary>No-op ASIO input until capture buffers are wired.</summary>
[SupportedOSPlatform("windows")]
internal sealed class AsioInput : IAudioInput
{
    private readonly AsioDeviceService _devices;

    public AsioInput(AsioDeviceService devices) => _devices = devices;

    public AudioFormat Format { get; } = new(48000, 2);
    public bool IsCapturing { get; private set; }

    public void Start(AudioCaptureCallback callback)
    {
        _ = callback;
        if (!_devices.DriverPresent || _devices.SelectedInput is null) return;
        IsCapturing = true;
    }

    public void Stop() => IsCapturing = false;

    public void Dispose() => Stop();
}
