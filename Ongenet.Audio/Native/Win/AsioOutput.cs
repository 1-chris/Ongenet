using System;
using System.Runtime.Versioning;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native.Win;

/// <summary>No-op ASIO output until a real buffer callback is wired.</summary>
[SupportedOSPlatform("windows")]
internal sealed class AsioOutput : IAudioOutput
{
    private readonly AsioDeviceService _devices;
    private AudioRenderCallback? _callback;

    public AsioOutput(AsioDeviceService devices) => _devices = devices;

    public AudioFormat Format { get; } = new(48000, 2);
    public event Action? FormatChanged { add { } remove { } }
    public bool IsRunning { get; private set; }
    public int SampleRate => 48000;
    public int BufferSize => 512;

    public void Start(AudioRenderCallback callback)
    {
        if (!_devices.DriverPresent || _devices.SelectedOutput is null) return;
        _callback = callback;
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
        _callback = null;
    }

    public void Dispose() => Stop();
}
