using System;
using System.Runtime.InteropServices;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio;

/// <summary>
/// <see cref="IAudioInput"/> backed by PortAudio. Opens the input device chosen in
/// <see cref="IAudioDeviceService"/> (or the system default) as an independent capture stream and
/// pushes each captured block to the registered callback from PortAudio's audio thread.
/// </summary>
public sealed class PortAudioInput : IAudioInput
{
    private const int RequestedSampleRate = 44100;
    private const int MaxChannels = 2;
    private const uint FramesPerBuffer = 512;

    private readonly object _lock = new();
    private readonly IAudioDeviceService _devices;

    private PortAudioNative.PaStreamCallback? _nativeCallback;
    private AudioCaptureCallback? _onAudio;
    private IntPtr _stream;
    private bool _paReferenced;
    private int _channels = 1;

    public PortAudioInput(IAudioDeviceService devices) => _devices = devices;

    public AudioFormat Format { get; private set; } = new(RequestedSampleRate, 1);

    public bool IsCapturing { get; private set; }

    public void Start(AudioCaptureCallback onAudio)
    {
        lock (_lock)
        {
            if (IsCapturing) return;

            if (!_paReferenced)
            {
                PortAudioNative.PaRef();
                _paReferenced = true;
            }

            _onAudio = onAudio;
            _nativeCallback = OnPortAudioCallback;
            OpenStream();
            IsCapturing = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsCapturing) return;
            IsCapturing = false;
            if (_stream != IntPtr.Zero)
            {
                PortAudioNative.Pa_StopStream(_stream);
                PortAudioNative.Pa_CloseStream(_stream);
                _stream = IntPtr.Zero;
            }

            _onAudio = null;
            _nativeCallback = null;
        }
    }

    private unsafe void OpenStream()
    {
        var deviceIndex = _devices.SelectedInput?.Index ?? PortAudioNative.Pa_GetDefaultInputDevice();
        if (deviceIndex < 0) throw new InvalidOperationException("No audio input device available.");

        var maxIn = _devices.SelectedInput?.MaxInputChannels ?? MaxChannels;
        // Mono mode captures a single channel (stored mono → plays centered at full level); Stereo
        // captures up to two channels as the device provides them.
        var desired = _devices.InputChannelMode == AudioInputChannelMode.Mono ? 1 : Math.Min(MaxChannels, maxIn);
        _channels = Math.Clamp(desired <= 0 ? 1 : desired, 1, MaxChannels);

        var info = PortAudioNative.Pa_GetDeviceInfo(deviceIndex);
        var latency = 0.0;
        var deviceRate = (double)RequestedSampleRate;
        if (info != IntPtr.Zero)
        {
            var di = Marshal.PtrToStructure<PortAudioNative.PaDeviceInfo>(info);
            latency = di.defaultLowInputLatency;
            deviceRate = di.defaultSampleRate;
        }

        var inParams = new PortAudioNative.PaStreamParameters
        {
            device = deviceIndex,
            channelCount = _channels,
            sampleFormat = PortAudioNative.PaFloat32,
            suggestedLatency = latency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        var rate = (double)RequestedSampleRate;
        var code = PortAudioNative.Pa_OpenStream(out _stream, (IntPtr)(&inParams), IntPtr.Zero,
            rate, FramesPerBuffer, PortAudioNative.PaNoFlag, _nativeCallback!, IntPtr.Zero);
        if (code < 0 && deviceRate > 0 && Math.Abs(deviceRate - rate) > 1)
        {
            rate = deviceRate;
            code = PortAudioNative.Pa_OpenStream(out _stream, (IntPtr)(&inParams), IntPtr.Zero,
                rate, FramesPerBuffer, PortAudioNative.PaNoFlag, _nativeCallback!, IntPtr.Zero);
        }

        Check(code, "Pa_OpenStream (input)");
        Check(PortAudioNative.Pa_StartStream(_stream), "Pa_StartStream (input)");
        Format = new AudioFormat((int)Math.Round(rate), _channels);
    }

    private unsafe int OnPortAudioCallback(
        IntPtr input, IntPtr output, uint frameCount, IntPtr timeInfo, uint statusFlags, IntPtr userData)
    {
        try
        {
            var onAudio = _onAudio;
            if (onAudio is not null && input != IntPtr.Zero)
            {
                var sampleCount = (int)frameCount * _channels;
                var span = new ReadOnlySpan<float>((void*)input, sampleCount);
                onAudio(span, _channels);
            }
        }
        catch
        {
            // Never let a managed exception escape into native code.
        }

        return PortAudioNative.PaContinue;
    }

    public void Dispose()
    {
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
        if (code < 0) throw new InvalidOperationException($"{operation} failed: {PortAudioNative.ErrorText(code)}");
    }
}
