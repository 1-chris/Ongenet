using System;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// <see cref="IAudioOutput"/> for the native backend. Opens a playback stream on the selected output
/// device through that device's subsystem driver, and reopens it when the selection changes.
/// The render callback is handed straight to the driver's audio thread.
/// </summary>
internal sealed class NativeAudioOutput : IAudioOutput
{
    private const int DesiredChannels = 2;

    private readonly object _lock = new();
    private readonly NativeDriverRegistry _drivers;
    private readonly IAudioDeviceService _devices;

    private AudioRenderCallback? _render;
    private INativeStream? _stream;

    public NativeAudioOutput(NativeDriverRegistry drivers, IAudioDeviceService devices)
    {
        _drivers = drivers;
        _devices = devices;
        _devices.OutputChanged += OnOutputDeviceChanged;
    }

    public AudioFormat Format { get; private set; } = AudioFormat.Default;
    public event Action? FormatChanged;
    public bool IsRunning { get; private set; }

    public void Start(AudioRenderCallback callback)
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _render = callback;
            OpenStream();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            CloseStream();
            _render = null;
        }
    }

    private static readonly bool Debug = Environment.GetEnvironmentVariable("ONGENET_ALSA_DEBUG") == "1";

    // Opens (or reopens) the stream on the currently selected output device. Caller holds _lock.
    private void OpenStream()
    {
        var device = _devices.SelectedOutput ?? throw new InvalidOperationException("No native output device available.");
        var driver = _drivers.For(device) ?? throw new InvalidOperationException($"No driver for host API '{device.HostApi}'.");

        if (Debug) Console.Error.WriteLine($"[native-out] opening '{device.DisplayName}' id={device.Id} via {driver.HostApi}");
        _stream = driver.OpenOutput(device, DesiredChannels, _render!);
        var newFormat = _stream.Format;
        var changed = newFormat != Format;
        Format = newFormat;
        if (changed) FormatChanged?.Invoke();
    }

    private void CloseStream()
    {
        _stream?.Dispose();
        _stream = null;
    }

    private void OnOutputDeviceChanged()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            CloseStream();
            try { OpenStream(); }
            catch { IsRunning = false; } // leave the engine running silently if the new device won't open
        }
    }

    public void Dispose()
    {
        _devices.OutputChanged -= OnOutputDeviceChanged;
        Stop();
    }
}
