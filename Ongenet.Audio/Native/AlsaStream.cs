using System;
using System.Runtime.InteropServices;
using System.Threading;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// A running ALSA PCM stream (playback or capture) on its own dedicated audio thread. Opens the device
/// as <c>SND_PCM_FORMAT_FLOAT_LE</c> interleaved — identical to the engine's native sample layout, so
/// no per-sample conversion happens — at a small period for low latency, and pumps the render/capture
/// callback in a tight loop with XRUN recovery. The block buffer is allocated and pinned once.
/// </summary>
internal sealed class AlsaStream : INativeStream
{
    // Requested period; ALSA negotiates to the nearest the device supports. ~256 frames ≈ 5.8 ms @ 44.1k.
    private const ulong RequestedPeriod = 256;
    private const int RequestedRate = 48000;

    private readonly bool _playback;
    private readonly AudioRenderCallback? _render;
    private readonly AudioCaptureCallback? _capture;

    private readonly IntPtr _pcm;
    private readonly int _channels;
    private readonly int _periodFrames;
    private readonly float[] _buffer;
    private GCHandle _pin;
    private readonly IntPtr _bufPtr;

    private readonly Thread _thread;
    private volatile bool _running;

    public AudioFormat Format { get; }

    private AlsaStream(bool playback, IntPtr pcm, int channels, int rate, int periodFrames,
        AudioRenderCallback? render, AudioCaptureCallback? capture)
    {
        _playback = playback;
        _pcm = pcm;
        _channels = channels;
        _periodFrames = periodFrames;
        _render = render;
        _capture = capture;
        Format = new AudioFormat(rate, channels);

        _buffer = new float[periodFrames * channels];
        _pin = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        _bufPtr = _pin.AddrOfPinnedObject();

        _running = true;
        _thread = new Thread(playback ? PlaybackLoop : CaptureLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest, // TODO: upgrade to SCHED_FIFO via pthread_setschedparam
            Name = playback ? "alsa-out" : "alsa-in",
        };
        _thread.Start();
    }

    /// <summary>Opens <paramref name="pcmName"/> (an ALSA PCM name like "default" or "hw:0,0") and starts the thread.</summary>
    public static AlsaStream Open(string pcmName, bool playback, int channels,
        AudioRenderCallback? render, AudioCaptureCallback? capture)
    {
        var stream = playback ? AlsaPcmNative.SND_PCM_STREAM_PLAYBACK : AlsaPcmNative.SND_PCM_STREAM_CAPTURE;
        Check(AlsaPcmNative.snd_pcm_open(out var pcm, pcmName, stream, 0), "snd_pcm_open");

        try
        {
            var (rate, ch, period) = Configure(pcm, channels);
            return new AlsaStream(playback, pcm, ch, rate, period, render, capture);
        }
        catch
        {
            AlsaPcmNative.snd_pcm_close(pcm);
            throw;
        }
    }

    // Negotiates float32 interleaved, channels, rate and a small period; returns the actual values.
    private static (int rate, int channels, int period) Configure(IntPtr pcm, int desiredChannels)
    {
        Check(AlsaPcmNative.snd_pcm_hw_params_malloc(out var hw), "hw_params_malloc");
        try
        {
            Check(AlsaPcmNative.snd_pcm_hw_params_any(pcm, hw), "hw_params_any");
            Check(AlsaPcmNative.snd_pcm_hw_params_set_access(pcm, hw, AlsaPcmNative.SND_PCM_ACCESS_RW_INTERLEAVED), "set_access");
            Check(AlsaPcmNative.snd_pcm_hw_params_set_format(pcm, hw, AlsaPcmNative.SND_PCM_FORMAT_FLOAT_LE), "set_format");

            var ch = (uint)Math.Max(1, desiredChannels);
            Check(AlsaPcmNative.snd_pcm_hw_params_set_channels_near(pcm, hw, ref ch), "set_channels_near");

            var rate = (uint)RequestedRate;
            var dir = 0;
            Check(AlsaPcmNative.snd_pcm_hw_params_set_rate_near(pcm, hw, ref rate, ref dir), "set_rate_near");

            var period = RequestedPeriod;
            dir = 0;
            Check(AlsaPcmNative.snd_pcm_hw_params_set_period_size_near(pcm, hw, ref period, ref dir), "set_period_size_near");

            // Buffer of ~4 periods keeps the device fed without adding much latency.
            var bufFrames = period * 4;
            Check(AlsaPcmNative.snd_pcm_hw_params_set_buffer_size_near(pcm, hw, ref bufFrames), "set_buffer_size_near");

            Check(AlsaPcmNative.snd_pcm_hw_params(pcm, hw), "hw_params (commit)");

            AlsaPcmNative.snd_pcm_hw_params_get_period_size(hw, out var actualPeriod, out _);
            if (actualPeriod == 0) actualPeriod = period;

            ConfigureSw(pcm, actualPeriod);

            Check(AlsaPcmNative.snd_pcm_prepare(pcm), "snd_pcm_prepare");
            return ((int)rate, (int)ch, (int)actualPeriod);
        }
        finally
        {
            AlsaPcmNative.snd_pcm_hw_params_free(hw);
        }
    }

    // Start playback as soon as one period is buffered; wake the thread when a period's worth is available.
    private static void ConfigureSw(IntPtr pcm, ulong period)
    {
        if (AlsaPcmNative.snd_pcm_sw_params_malloc(out var sw) < 0) return;
        try
        {
            AlsaPcmNative.snd_pcm_sw_params_current(pcm, sw);
            AlsaPcmNative.snd_pcm_sw_params_set_start_threshold(pcm, sw, period);
            AlsaPcmNative.snd_pcm_sw_params_set_avail_min(pcm, sw, period);
            AlsaPcmNative.snd_pcm_sw_params(pcm, sw);
        }
        finally
        {
            AlsaPcmNative.snd_pcm_sw_params_free(sw);
        }
    }

    private static readonly bool Debug = Environment.GetEnvironmentVariable("ONGENET_ALSA_DEBUG") == "1";
    private long _dbgBlocks;
    private long _dbgWrites;
    private long _dbgErrors;
    private float _dbgPeak;

    private unsafe void PlaybackLoop()
    {
        if (Debug) Console.Error.WriteLine($"[alsa-out] thread started, {Format.SampleRate}x{Format.Channels}, period={_periodFrames}");
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

            if (Debug)
            {
                float peak = 0f;
                for (var i = 0; i < span.Length; i++) { var a = span[i] < 0 ? -span[i] : span[i]; if (a > peak) peak = a; }
                if (peak > _dbgPeak) _dbgPeak = peak;
                if ((++_dbgBlocks % 100) == 0)
                    Console.Error.WriteLine($"[alsa-out] blocks={_dbgBlocks} writes={_dbgWrites} errs={_dbgErrors} peakSinceLast={_dbgPeak:F4}");
                if ((_dbgBlocks % 100) == 0) _dbgPeak = 0f;
            }

            WriteAll();
        }

        if (Debug) Console.Error.WriteLine($"[alsa-out] thread EXIT (running={_running}) blocks={_dbgBlocks} errs={_dbgErrors}");
        AlsaPcmNative.snd_pcm_drop(_pcm);
    }

    // Writes the whole block, advancing past partial writes and recovering from XRUNs.
    private void WriteAll()
    {
        var offsetFrames = 0;
        while (_running && offsetFrames < _periodFrames)
        {
            var ptr = _bufPtr + offsetFrames * _channels * sizeof(float);
            var n = AlsaPcmNative.snd_pcm_writei(_pcm, ptr, (ulong)(_periodFrames - offsetFrames));
            if (n >= 0)
            {
                offsetFrames += (int)n;
                if (Debug) _dbgWrites++;
            }
            else
            {
                if (Debug) { _dbgErrors++; if (_dbgErrors <= 5) Console.Error.WriteLine($"[alsa-out] writei err {n} ({AlsaPcmNative.ErrorText((int)n)})"); }
                if (!Recover((int)n))
                {
                    if (Debug) Console.Error.WriteLine($"[alsa-out] UNRECOVERABLE {n} — stopping thread");
                    _running = false; // unrecoverable — stop the thread (engine keeps running silently)
                }
            }
        }
    }

    private unsafe void CaptureLoop()
    {
        while (_running)
        {
            var offsetFrames = 0;
            while (_running && offsetFrames < _periodFrames)
            {
                var ptr = _bufPtr + offsetFrames * _channels * sizeof(float);
                var n = AlsaPcmNative.snd_pcm_readi(_pcm, ptr, (ulong)(_periodFrames - offsetFrames));
                if (n >= 0)
                {
                    offsetFrames += (int)n;
                }
                else if (!Recover((int)n))
                {
                    _running = false;
                }
            }

            if (offsetFrames <= 0) continue;
            try
            {
                var capture = _capture;
                if (capture is not null)
                {
                    var span = new ReadOnlySpan<float>((void*)_bufPtr, offsetFrames * _channels);
                    capture(span, _channels);
                }
            }
            catch
            {
                // Never let a managed exception escape onto the audio thread.
            }
        }

        AlsaPcmNative.snd_pcm_drop(_pcm);
    }

    // Attempts to recover from a negated-errno failure; returns false if recovery itself fails.
    private bool Recover(int negErrno)
        => AlsaPcmNative.snd_pcm_recover(_pcm, negErrno, 1) >= 0;

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive && _thread != Thread.CurrentThread)
            _thread.Join(500);

        AlsaPcmNative.snd_pcm_close(_pcm);
        if (_pin.IsAllocated) _pin.Free();
    }

    private static void Check(int code, string op)
    {
        if (code < 0) throw new InvalidOperationException($"ALSA {op} failed: {AlsaPcmNative.ErrorText(code)}");
    }
}
