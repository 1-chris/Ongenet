using System;
using System.Runtime.InteropServices;
using System.Threading;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// A running PulseAudio stream (playback or capture) on its own dedicated thread, driven by the
/// blocking simple API. Opens as float32 interleaved (the engine's native layout — no conversion) at a
/// fixed rate the server resamples to the device. PulseAudio mixes our stream with other apps' audio
/// (no exclusive hardware grab, unlike the ALSA hardware path) and follows the chosen sink/source.
/// </summary>
internal sealed class PulseStream : INativeStream
{
    private const int RequestedRate = 48000;
    private const int BlockFrames = 512;

    private readonly bool _playback;
    private readonly AudioRenderCallback? _render;
    private readonly AudioCaptureCallback? _capture;
    private readonly IntPtr _simple;
    private readonly int _channels;
    private readonly float[] _buffer;
    private GCHandle _pin;
    private readonly IntPtr _bufPtr;
    private readonly UIntPtr _byteCount;

    private readonly Thread _thread;
    private volatile bool _running;

    public AudioFormat Format { get; }

    private PulseStream(bool playback, IntPtr simple, int channels, AudioRenderCallback? render, AudioCaptureCallback? capture)
    {
        _playback = playback;
        _simple = simple;
        _channels = channels;
        _render = render;
        _capture = capture;
        Format = new AudioFormat(RequestedRate, channels);

        _buffer = new float[BlockFrames * channels];
        _pin = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        _bufPtr = _pin.AddrOfPinnedObject();
        _byteCount = (UIntPtr)(_buffer.Length * sizeof(float));

        _running = true;
        _thread = new Thread(playback ? PlaybackLoop : CaptureLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = playback ? "pulse-out" : "pulse-in",
        };
        _thread.Start();
    }

    /// <summary>Opens a stream on <paramref name="device"/> (a Pulse sink/source name, or null for the default).</summary>
    public static PulseStream Open(string? device, bool playback, int channels,
        AudioRenderCallback? render, AudioCaptureCallback? capture)
    {
        var ss = new PulseAudioNative.pa_sample_spec
        {
            format = PulseAudioNative.PA_SAMPLE_FLOAT32LE,
            rate = RequestedRate,
            channels = (byte)Math.Clamp(channels, 1, 8),
        };
        var dir = playback ? PulseAudioNative.PA_STREAM_PLAYBACK : PulseAudioNative.PA_STREAM_RECORD;
        var simple = PulseAudioNative.pa_simple_new(null, "Ongenet", dir, device, playback ? "playback" : "capture",
            ref ss, IntPtr.Zero, IntPtr.Zero, out var error);
        if (simple == IntPtr.Zero)
            throw new InvalidOperationException($"pa_simple_new failed: {PulseAudioNative.ErrorText(error)}");

        return new PulseStream(playback, simple, ss.channels, render, capture);
    }

    private unsafe void PlaybackLoop()
    {
        var span = new Span<float>((void*)_bufPtr, _buffer.Length);
        while (_running)
        {
            try
            {
                var render = _render;
                if (render is not null) render(span);
                else span.Clear();
            }
            catch
            {
                span.Clear();
            }

            if (PulseAudioNative.pa_simple_write(_simple, _bufPtr, _byteCount, out _) < 0)
                _running = false; // server gone — stop quietly (engine keeps running silently)
        }
    }

    private unsafe void CaptureLoop()
    {
        while (_running)
        {
            if (PulseAudioNative.pa_simple_read(_simple, _bufPtr, _byteCount, out _) < 0)
            {
                _running = false;
                break;
            }

            try
            {
                var capture = _capture;
                if (capture is not null)
                {
                    var span = new ReadOnlySpan<float>((void*)_bufPtr, _buffer.Length);
                    capture(span, _channels);
                }
            }
            catch
            {
                // Never let a managed exception escape onto the audio thread.
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive && _thread != Thread.CurrentThread)
            _thread.Join(500);

        if (_playback) PulseAudioNative.pa_simple_drain(_simple, out _);
        PulseAudioNative.pa_simple_free(_simple);
        if (_pin.IsAllocated) _pin.Free();
    }
}
