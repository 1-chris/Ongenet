using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ongenet.Core.Audio;
using AA = Ongenet.Audio.Interop.AAudioNative;

namespace Ongenet.Audio.Native.Android;

/// <summary>
/// The Android native <see cref="IAudioBackend"/>: plays through AAudio (<c>libaaudio.so</c>) via
/// <see cref="AndroidAudioOutput"/>. Shares the id "native" with the desktop backends so "Native" is the
/// same audio-system option on every OS; the composition root registers whichever one matches the running
/// OS. Capture is not yet implemented (a stub <see cref="AndroidAudioInput"/>), and device enumeration is a
/// no-op — AAudio routes to the system default output the user has chosen.
/// </summary>
public sealed class AndroidNativeBackend : IAudioBackend
{
    private readonly AndroidAudioDeviceService _devices = new();
    private readonly AndroidAudioOutput _output = new();
    private readonly AndroidAudioInput _input = new();

    public string Id => "native";
    public string DisplayName => "Native (AAudio)";
    public bool IsSupported => OperatingSystem.IsAndroid();

    public IAudioDeviceService Devices => _devices;
    public IAudioOutput Output => _output;
    public IAudioInput Input => _input;

    public void Dispose()
    {
        _output.Dispose();
        _input.Dispose();
    }
}

/// <summary>
/// <see cref="IAudioOutput"/> for Android, backed by a low-latency AAudio output stream opened as float32
/// interleaved (the engine's native layout). AAudio pulls blocks from <see cref="DataCallback"/> on its
/// real-time audio thread, which renders straight into the device buffer. Device selection is not exposed
/// (AAudio uses the system default route), so there is no reopen-on-device-change path.
/// </summary>
internal sealed unsafe class AndroidAudioOutput : IAudioOutput
{
    // Requested defaults; AAudio may hand back a different rate/channel count, which we read back and
    // surface through Format so the engine re-prepares its DSP.
    private const int Rate = 48000;
    private const int Channels = 2;

    private readonly object _lock = new();
    private AudioRenderCallback? _render;
    private IntPtr _stream;
    private GCHandle _self;
    private int _channels = Channels;

    public AudioFormat Format { get; private set; } = new(Rate, Channels);
    public event Action? FormatChanged;
    public bool IsRunning { get; private set; }

    public void Start(AudioRenderCallback callback)
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _render = callback;
            Open();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            Close();
            _render = null;
        }
    }

    private void Open()
    {
        if (AA.AAudio_createStreamBuilder(out var builder) != AA.OK || builder == IntPtr.Zero)
            throw new InvalidOperationException("AAudio_createStreamBuilder failed.");

        try
        {
            AA.AAudioStreamBuilder_setDirection(builder, AA.DIRECTION_OUTPUT);
            AA.AAudioStreamBuilder_setFormat(builder, AA.FORMAT_PCM_FLOAT);
            AA.AAudioStreamBuilder_setChannelCount(builder, Channels);
            AA.AAudioStreamBuilder_setSampleRate(builder, Rate);
            AA.AAudioStreamBuilder_setSharingMode(builder, AA.SHARING_MODE_SHARED);
            AA.AAudioStreamBuilder_setPerformanceMode(builder, AA.PERFORMANCE_MODE_LOW_LATENCY);

            _self = GCHandle.Alloc(this);
            AA.AAudioStreamBuilder_setDataCallback(builder, &DataCallback, GCHandle.ToIntPtr(_self));

            if (AA.AAudioStreamBuilder_openStream(builder, out _stream) != AA.OK || _stream == IntPtr.Zero)
                throw new InvalidOperationException("AAudioStreamBuilder_openStream failed.");

            // Read back what AAudio actually granted (it may differ from the request).
            var rate = AA.AAudioStream_getSampleRate(_stream);
            _channels = AA.AAudioStream_getChannelCount(_stream);
            if (_channels <= 0) _channels = Channels;

            var fmt = new AudioFormat(rate > 0 ? rate : Rate, _channels);
            var changed = fmt != Format;
            Format = fmt;

            if (AA.AAudioStream_requestStart(_stream) != AA.OK)
                throw new InvalidOperationException("AAudioStream_requestStart failed.");

            if (changed) FormatChanged?.Invoke();
        }
        finally
        {
            AA.AAudioStreamBuilder_delete(builder);
        }
    }

    private void Close()
    {
        if (_stream != IntPtr.Zero)
        {
            AA.AAudioStream_requestStop(_stream);
            AA.AAudioStream_close(_stream);
            _stream = IntPtr.Zero;
        }

        if (_self.IsAllocated) _self.Free();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int DataCallback(IntPtr stream, IntPtr userData, IntPtr audioData, int numFrames)
    {
        try
        {
            if (GCHandle.FromIntPtr(userData).Target is AndroidAudioOutput o)
                o.Render(audioData, numFrames);
        }
        catch
        {
            // Never let a managed exception escape onto AAudio's real-time thread.
        }

        return AA.CALLBACK_RESULT_CONTINUE;
    }

    private void Render(IntPtr audioData, int numFrames)
    {
        if (audioData == IntPtr.Zero || numFrames <= 0) return;

        var span = new Span<float>((void*)audioData, numFrames * _channels);
        var render = _render;
        if (render is not null) render(span);
        else span.Clear(); // no engine attached → output silence rather than uninitialised memory
    }

    public void Dispose() => Stop();
}

/// <summary>Capture is not yet implemented on Android (microphone input via AAudio is a future addition).</summary>
internal sealed class AndroidAudioInput : IAudioInput
{
    public AudioFormat Format => AudioFormat.Default;
    public bool IsCapturing => false;
    public void Start(AudioCaptureCallback onAudio) { /* no input yet */ }
    public void Stop() { }
    public void Dispose() { }
}

/// <summary>
/// No device enumeration on Android — AAudio routes to the system default output. Presents empty device
/// lists (the engine plays to the default route), mirroring the browser backend's device service.
/// </summary>
internal sealed class AndroidAudioDeviceService : IAudioDeviceService
{
    public IReadOnlyList<AudioDevice> InputDevices { get; } = Array.Empty<AudioDevice>();
    public IReadOnlyList<AudioDevice> OutputDevices { get; } = Array.Empty<AudioDevice>();

    public AudioDevice? SelectedOutput { get; set; }
    public AudioDevice? SelectedInput { get; set; }
    public AudioInputChannelMode InputChannelMode { get; set; } = AudioInputChannelMode.Stereo;

    public void Refresh() => DevicesChanged?.Invoke();

    public event Action? DevicesChanged;
    public event Action? OutputChanged;
    public event Action? InputChanged;
}
