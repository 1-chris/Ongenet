using System;
using System.IO;
using System.Threading;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// Per-voice disk streaming state for one streamed <see cref="SfzSample"/>: a single-producer
/// (streaming thread) / single-consumer (audio thread) ring buffer over the sample's float32 raw file,
/// plus the RAM preload that covers the attack. The audio thread only ever touches RAM
/// (<see cref="Read"/>); all file open/read/close happens on the streaming thread, so it never blocks.
/// </summary>
public sealed class SfzStream
{
    private const int CapacityFrames = 1 << 15; // 32768 frames of look-ahead per voice
    private const int FillChunkFrames = 4096;

    // State machine driven by the audio thread, serviced by the streaming thread.
    private const int Idle = 0, Requested = 1, Active = 2;
    private int _state = Idle;
    private long _generation;       // bumped per Request so the engine reopens for a new note

    // Configuration for the requested note (written by audio thread before going Requested).
    private SfzSample? _sample;
    private int _channels;
    private long _frameCount;
    private long _ringStart;        // first frame the ring serves (frames below come from preload)

    // Source PCM format (the stream reads native PCM from the original file and converts while filling).
    private long _dataOffset;
    private int _bits;
    private bool _isFloat;
    private int _bytesPerSample;
    private int _frameBytes;

    // Ring buffer (producer writes _filledTo forward; consumer advances _consumedTo).
    private float[] _buf = Array.Empty<float>();
    private volatile int _filledTo;     // ring-relative frame count produced from _ringStart
    private volatile int _consumedTo;   // ring-relative frame count the consumer no longer needs

    // Preload (RAM) for the attack.
    private float[]? _preload;
    private long _preloadFrames;

    // Engine-thread-only file state.
    private FileStream? _fs;
    private long _openGeneration = -1;
    private byte[] _io = Array.Empty<byte>();

    public bool IsServicing => Volatile.Read(ref _state) != Idle;

    // ---- Audio thread ----

    /// <summary>Requests streaming of <paramref name="sample"/> starting at <paramref name="startFrame"/>.</summary>
    public void Request(SfzSample sample, long startFrame)
    {
        _sample = sample;
        _channels = sample.Channels;
        _frameCount = sample.FrameCount;
        _preload = sample.Preload;
        _preloadFrames = sample.PreloadFrames;
        _dataOffset = sample.StreamDataOffset;
        _bits = sample.StreamBits;
        _isFloat = sample.StreamIsFloat;
        _bytesPerSample = _bits / 8;
        _frameBytes = _bytesPerSample * _channels;
        // Frames below the preload size come from RAM; the ring serves everything from there on (or from
        // the start frame if a large `offset` skips past the preload). Ring indices are (re)set by the
        // engine in Open(), so a reused voice can't race the engine by touching them here.
        _ringStart = startFrame < _preloadFrames ? _preloadFrames : startFrame;
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _state, Requested);
    }

    /// <summary>Releases the stream when the voice finishes (the engine closes the file off-thread).</summary>
    public void Release() => Volatile.Write(ref _state, Idle);

    /// <summary>Reads one interpolation tap (frame, channel). Never blocks; returns 0 outside the sample.</summary>
    public float Read(long frame, int channel)
    {
        if (frame < 0 || frame >= _frameCount) return 0f;

        if (frame < _ringStart)
        {
            var pl = _preload;
            if (pl is null) return 0f;
            var idx = frame * _channels + channel;
            return idx >= 0 && idx < pl.Length ? pl[idx] : 0f;
        }

        // Only trust the ring once the engine has (re)opened for this request — otherwise a reused voice
        // could read stale frames left from the previous note before Open() resets the indices.
        if (Volatile.Read(ref _state) != Active) return 0f;

        var rel = (int)(frame - _ringStart);
        if (rel < _consumedTo || rel >= _filledTo) return 0f; // not yet streamed (under-run) — silent
        var p = (rel % CapacityFrames) * _channels + channel;
        return _buf[p];
    }

    /// <summary>Tells the producer the consumer no longer needs frames below <paramref name="frame"/>.</summary>
    public void SetConsumed(long frame)
    {
        if (frame < _ringStart) return;
        var rel = (int)(frame - _ringStart);
        if (rel > _consumedTo) _consumedTo = rel;
    }

    // ---- Streaming thread (engine) ----

    /// <summary>One service step: open the file when newly requested, top up the ring, or close when idle.</summary>
    public void Service()
    {
        var state = Volatile.Read(ref _state);

        if (state == Idle)
        {
            if (_fs is not null) { _fs.Dispose(); _fs = null; _openGeneration = -1; }
            return;
        }

        if (state == Requested)
        {
            Open();
            Volatile.Write(ref _state, Active);
        }

        Fill();
    }

    /// <summary>Closes the file (engine shutdown / instrument unload).</summary>
    public void Close()
    {
        Volatile.Write(ref _state, Idle);
        _fs?.Dispose();
        _fs = null;
        _openGeneration = -1;
    }

    private void Open()
    {
        var gen = Interlocked.Read(ref _generation);
        if (_fs is not null && _openGeneration == gen) return;

        _fs?.Dispose();
        _fs = null;

        var path = _sample?.StreamPath;
        if (path is null) return;
        if (_buf.Length < CapacityFrames * _channels) _buf = new float[CapacityFrames * _channels];
        if (_io.Length < FillChunkFrames * _frameBytes) _io = new byte[FillChunkFrames * _frameBytes];

        _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        _fs.Seek(_dataOffset + _ringStart * _frameBytes, SeekOrigin.Begin);
        _filledTo = 0;     // ring indices are owned by the engine thread; reset them here, not in Request
        _consumedTo = 0;
        _openGeneration = gen;
    }

    private void Fill()
    {
        var fs = _fs;
        if (fs is null) return;

        // Producer reads consumer indices via volatile; only it writes _filledTo.
        while (true)
        {
            var consumed = _consumedTo;
            var filled = _filledTo;
            var absFilled = _ringStart + filled;
            if (absFilled >= _frameCount) return;                 // whole tail streamed
            if (filled - consumed >= CapacityFrames) return;      // ring full; wait for the consumer

            var framesFree = CapacityFrames - (filled - consumed);
            var framesLeft = (int)Math.Min(_frameCount - absFilled, framesFree);
            var toRead = Math.Min(FillChunkFrames, framesLeft);
            if (toRead <= 0) return;

            var got = ReadExact(fs, _io, toRead * _frameBytes);
            if (got <= 0) return;
            var framesGot = got / _frameBytes;
            if (framesGot <= 0) return;

            // Convert native PCM → float straight into the ring (per frame, so the wrap is handled).
            for (var i = 0; i < framesGot; i++)
            {
                var dstBase = ((filled + i) % CapacityFrames) * _channels;
                var srcBase = i * _frameBytes;
                for (var c = 0; c < _channels; c++)
                    _buf[dstBase + c] = WavLayout.ToFloat(_io, srcBase + c * _bytesPerSample, _bits, _isFloat);
            }

            _filledTo = filled + framesGot;
        }
    }

    private static int ReadExact(FileStream fs, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var n = fs.Read(buffer, total, count - total);
            if (n == 0) break;
            total += n;
        }

        return total;
    }
}
