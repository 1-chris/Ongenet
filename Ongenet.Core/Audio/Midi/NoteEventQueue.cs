using System;
using System.Collections.Concurrent;

namespace Ongenet.Core.Audio.Midi;

/// <summary>
/// A bounded, lock-free queue for note events pushed from any thread and drained on the audio thread.
/// </summary>
public sealed class NoteEventQueue<T> where T : struct
{
    private readonly ConcurrentQueue<T> _queue = new();
    private readonly T[] _drainBuf;
    private int _drainCount;

    public NoteEventQueue(int drainCapacity = 64)
    {
        _drainBuf = new T[Math.Max(8, drainCapacity)];
    }

    public void Enqueue(in T item) => _queue.Enqueue(item);

    /// <summary>Drains queued items into the internal buffer; returns the count (0 when empty).</summary>
    public ReadOnlySpan<T> Drain()
    {
        _drainCount = 0;
        while (_drainCount < _drainBuf.Length && _queue.TryDequeue(out var item))
            _drainBuf[_drainCount++] = item;
        return _drainBuf.AsSpan(0, _drainCount);
    }

    public bool IsEmpty => _queue.IsEmpty;
}
