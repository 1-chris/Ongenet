using System;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// <see cref="IAudioInput"/> for the native backend. Opens an independent capture stream on the selected
/// input device through that device's subsystem driver when recording starts (the chosen device
/// applies at the next <see cref="Start"/>).
/// </summary>
internal sealed class NativeAudioInput : IAudioInput
{
    private readonly object _lock = new();
    private readonly NativeDriverRegistry _drivers;
    private readonly IAudioDeviceService _devices;
    private INativeStream? _stream;

    public NativeAudioInput(NativeDriverRegistry drivers, IAudioDeviceService devices)
    {
        _drivers = drivers;
        _devices = devices;
    }

    public AudioFormat Format { get; private set; } = new(48000, 1);
    public bool IsCapturing { get; private set; }

    public void Start(AudioCaptureCallback onAudio)
    {
        lock (_lock)
        {
            if (IsCapturing) return;

            var device = _devices.SelectedInput ?? throw new InvalidOperationException("No native input device available.");
            var driver = _drivers.For(device) ?? throw new InvalidOperationException($"No driver for host API '{device.HostApi}'.");

            // Mono captures a single channel (stored mono → plays centered); Stereo captures two.
            var channels = _devices.InputChannelMode == AudioInputChannelMode.Mono ? 1 : 2;
            _stream = driver.OpenInput(device, channels, onAudio);
            Format = _stream.Format;
            IsCapturing = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsCapturing) return;
            IsCapturing = false;
            _stream?.Dispose();
            _stream = null;
        }
    }

    public void Dispose() => Stop();
}
