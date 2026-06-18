using System;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio;

/// <summary>
/// <see cref="IAudioOutput"/> backed by PortAudio. Opens the default output device as 44.1 kHz
/// stereo float and pumps the engine's render callback from PortAudio's audio thread.
/// </summary>
public sealed class PortAudioOutput : IAudioOutput
{
    private const int RequestedSampleRate = 44100;
    private const int Channels = 2;
    private const uint FramesPerBuffer = 512;

    private readonly object _lock = new();

    // Held in a field so the GC can't collect the delegate while PortAudio holds the pointer.
    private PortAudioNative.PaStreamCallback? _nativeCallback;
    private AudioRenderCallback? _render;
    private IntPtr _stream;
    private bool _paInitialized;

    public AudioFormat Format { get; private set; } = new(RequestedSampleRate, Channels);

    public bool IsRunning { get; private set; }

    public void Start(AudioRenderCallback callback)
    {
        lock (_lock)
        {
            if (IsRunning) return;

            PortAudioNative.EnsureResolver();

            if (!_paInitialized)
            {
                Check(PortAudioNative.Pa_Initialize(), "Pa_Initialize");
                _paInitialized = true;
            }

            _render = callback;
            _nativeCallback = OnPortAudioCallback;

            Check(PortAudioNative.Pa_OpenDefaultStream(
                out _stream,
                numInputChannels: 0,
                numOutputChannels: Channels,
                sampleFormat: PortAudioNative.PaFloat32,
                sampleRate: RequestedSampleRate,
                framesPerBuffer: FramesPerBuffer,
                streamCallback: _nativeCallback,
                userData: IntPtr.Zero), "Pa_OpenDefaultStream");

            Check(PortAudioNative.Pa_StartStream(_stream), "Pa_StartStream");

            Format = new AudioFormat(RequestedSampleRate, Channels);
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;

            if (_stream != IntPtr.Zero)
            {
                PortAudioNative.Pa_StopStream(_stream);
                PortAudioNative.Pa_CloseStream(_stream);
                _stream = IntPtr.Zero;
            }

            _render = null;
            _nativeCallback = null;
        }
    }

    private unsafe int OnPortAudioCallback(
        IntPtr input, IntPtr output, uint frameCount, IntPtr timeInfo, uint statusFlags, IntPtr userData)
    {
        var sampleCount = (int)frameCount * Channels;
        var buffer = new Span<float>((void*)output, sampleCount);

        try
        {
            var render = _render;
            if (render is not null)
            {
                render(buffer);
            }
            else
            {
                buffer.Clear();
            }
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
        Stop();
        lock (_lock)
        {
            if (_paInitialized)
            {
                PortAudioNative.Pa_Terminate();
                _paInitialized = false;
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
