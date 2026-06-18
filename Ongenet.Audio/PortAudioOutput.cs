using System;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio;

/// <summary>
/// <see cref="IAudioOutput"/> backed by PortAudio. Opens the output device chosen in
/// <see cref="IAudioDeviceService"/> (or the system default) as float stereo and pumps the engine's
/// render callback from PortAudio's audio thread. Reopens the stream when the selected device changes.
/// </summary>
public sealed class PortAudioOutput : IAudioOutput
{
    private const int RequestedSampleRate = 44100;
    private const int MaxChannels = 2;
    private const uint FramesPerBuffer = 512;

    private readonly object _lock = new();
    private readonly IAudioDeviceService _devices;

    // Held in a field so the GC can't collect the delegate while PortAudio holds the pointer.
    private PortAudioNative.PaStreamCallback? _nativeCallback;
    private AudioRenderCallback? _render;
    private IntPtr _stream;
    private bool _paReferenced;
    private int _channels = MaxChannels;

    public PortAudioOutput(IAudioDeviceService devices)
    {
        _devices = devices;
        _devices.OutputChanged += OnOutputDeviceChanged;
    }

    public AudioFormat Format { get; private set; } = new(RequestedSampleRate, MaxChannels);

    public bool IsRunning { get; private set; }

    public void Start(AudioRenderCallback callback)
    {
        lock (_lock)
        {
            if (IsRunning) return;

            if (!_paReferenced)
            {
                PortAudioNative.PaRef();
                _paReferenced = true;
            }

            _render = callback;
            _nativeCallback = OnPortAudioCallback;
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
            _nativeCallback = null;
        }
    }

    // Opens (or reopens) the stream on the currently selected output device. Caller holds _lock.
    private unsafe void OpenStream()
    {
        var deviceIndex = _devices.SelectedOutput?.Index ?? PortAudioNative.Pa_GetDefaultOutputDevice();
        if (deviceIndex < 0) throw new InvalidOperationException("No audio output device available.");

        var maxOut = _devices.SelectedOutput?.MaxOutputChannels ?? MaxChannels;
        _channels = Math.Clamp(maxOut <= 0 ? MaxChannels : maxOut, 1, MaxChannels);

        var info = PortAudioNative.Pa_GetDeviceInfo(deviceIndex);
        var latency = 0.0;
        var deviceRate = (double)RequestedSampleRate;
        if (info != IntPtr.Zero)
        {
            var di = System.Runtime.InteropServices.Marshal.PtrToStructure<PortAudioNative.PaDeviceInfo>(info);
            latency = di.defaultLowOutputLatency;
            deviceRate = di.defaultSampleRate;
        }

        var outParams = new PortAudioNative.PaStreamParameters
        {
            device = deviceIndex,
            channelCount = _channels,
            sampleFormat = PortAudioNative.PaFloat32,
            suggestedLatency = latency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        // Prefer 44.1 kHz; if the device rejects it, fall back to its own default rate.
        var rate = (double)RequestedSampleRate;
        var code = PortAudioNative.Pa_OpenStream(out _stream, IntPtr.Zero, (IntPtr)(&outParams),
            rate, FramesPerBuffer, PortAudioNative.PaNoFlag, _nativeCallback!, IntPtr.Zero);
        if (code < 0 && deviceRate > 0 && Math.Abs(deviceRate - rate) > 1)
        {
            rate = deviceRate;
            code = PortAudioNative.Pa_OpenStream(out _stream, IntPtr.Zero, (IntPtr)(&outParams),
                rate, FramesPerBuffer, PortAudioNative.PaNoFlag, _nativeCallback!, IntPtr.Zero);
        }

        Check(code, "Pa_OpenStream");
        Check(PortAudioNative.Pa_StartStream(_stream), "Pa_StartStream");
        Format = new AudioFormat((int)Math.Round(rate), _channels);
    }

    private void CloseStream()
    {
        if (_stream != IntPtr.Zero)
        {
            PortAudioNative.Pa_StopStream(_stream);
            PortAudioNative.Pa_CloseStream(_stream);
            _stream = IntPtr.Zero;
        }
    }

    private void OnOutputDeviceChanged()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            CloseStream();
            try
            {
                OpenStream();
            }
            catch
            {
                // Leave the engine running silently rather than crashing if the new device won't open.
                IsRunning = false;
            }
        }
    }

    private unsafe int OnPortAudioCallback(
        IntPtr input, IntPtr output, uint frameCount, IntPtr timeInfo, uint statusFlags, IntPtr userData)
    {
        var sampleCount = (int)frameCount * _channels;
        var buffer = new Span<float>((void*)output, sampleCount);

        try
        {
            var render = _render;
            if (render is not null) render(buffer);
            else buffer.Clear();
        }
        catch
        {
            // Never let a managed exception escape into native code; output silence instead.
            buffer.Clear();
        }

        return PortAudioNative.PaContinue;
    }

    public void Dispose()
    {
        _devices.OutputChanged -= OnOutputDeviceChanged;
        Stop();
        lock (_lock)
        {
            if (_paReferenced)
            {
                PortAudioNative.PaUnref();
                _paReferenced = false;
            }
        }
    }

    private static void Check(int code, string operation)
    {
        // paNoError == 0; negative codes are errors.
        if (code < 0)
        {
            throw new InvalidOperationException($"{operation} failed: {PortAudioNative.ErrorText(code)}");
        }
    }
}
