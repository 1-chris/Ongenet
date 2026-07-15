using System;
using System.Threading;

namespace Ongenet.Core.Audio.Midi;

/// <summary>
/// A bounded, lock-free queue for note events pushed from any thread and drained on the audio thread.
/// </summary>
public sealed class NoteEventQueue<T> where T : struct
{
    private struct Slot
    {
        public long Sequence;
        public T Item;
    }

    private readonly Slot[] _slots;
    private readonly int _mask;
    private readonly T[] _drainBuf;
    private int _drainCount;
    private long _enqueuePosition;
    private long _dequeuePosition;

    public NoteEventQueue(int drainCapacity = 64)
    {
        var requested = Math.Max(8, drainCapacity);
        var capacity = 1;
        while (capacity < requested * 4) capacity <<= 1;
        _slots = new Slot[capacity];
        _mask = capacity - 1;
        for (var i = 0; i < capacity; i++) _slots[i].Sequence = i;
        _drainBuf = new T[requested];
    }

    public void Enqueue(in T item)
    {
        while (true)
        {
            var position = Volatile.Read(ref _enqueuePosition);
            ref var slot = ref _slots[(int)position & _mask];
            var sequence = Volatile.Read(ref slot.Sequence);
            var difference = sequence - position;
            if (difference == 0)
            {
                if (Interlocked.CompareExchange(ref _enqueuePosition, position + 1, position) != position)
                    continue;
                slot.Item = item;
                Volatile.Write(ref slot.Sequence, position + 1);
                return;
            }

            // Bounded realtime queue: discard a new event rather than allocate or block the producer.
            if (difference < 0) return;
            Thread.SpinWait(1);
        }
    }

    /// <summary>Drains queued items into the internal buffer; returns the count (0 when empty).</summary>
    public ReadOnlySpan<T> Drain()
    {
        _drainCount = 0;
        while (_drainCount < _drainBuf.Length)
        {
            var position = _dequeuePosition;
            ref var slot = ref _slots[(int)position & _mask];
            var sequence = Volatile.Read(ref slot.Sequence);
            if (sequence - (position + 1) != 0) break;
            _drainBuf[_drainCount++] = slot.Item;
            Volatile.Write(ref slot.Sequence, position + _slots.Length);
            _dequeuePosition = position + 1;
        }
        return _drainBuf.AsSpan(0, _drainCount);
    }

    public bool IsEmpty
    {
        get
        {
            var position = Volatile.Read(ref _dequeuePosition);
            ref var slot = ref _slots[(int)position & _mask];
            return Volatile.Read(ref slot.Sequence) - (position + 1) != 0;
        }
    }
}
