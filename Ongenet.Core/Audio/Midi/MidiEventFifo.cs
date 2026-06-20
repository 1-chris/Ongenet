using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Midi;

/// <summary>
/// A small thread-safe MIDI message queue for effects/instruments that receive input off the audio
/// thread. Producers (the live MIDI backend, the UI, the clip sequencer) call <see cref="Push"/>;
/// the audio thread calls <see cref="Drain"/> once per block and processes the snapshot. A plain lock
/// over a growable buffer — MIDI traffic is light, so contention is negligible, and after warm-up the
/// drain target list is reused so steady-state processing allocates nothing. Reusable by any
/// <see cref="Ongenet.Core.Audio.Effects.IMidiAwareEffect"/>.
/// </summary>
public sealed class MidiEventFifo
{
    private readonly object _lock = new();
    private MidiMessage[] _buf;
    private int _count;

    public MidiEventFifo(int capacity = 128) => _buf = new MidiMessage[Math.Max(8, capacity)];

    /// <summary>Enqueues a message. Safe to call from any thread.</summary>
    public void Push(in MidiMessage message)
    {
        lock (_lock)
        {
            if (_count >= _buf.Length) Array.Resize(ref _buf, _buf.Length * 2);
            _buf[_count++] = message;
        }
    }

    /// <summary>
    /// Moves all queued messages into <paramref name="dest"/> (cleared first), in arrival order, and
    /// empties the queue. Call from the audio thread at the start of a block.
    /// </summary>
    public void Drain(List<MidiMessage> dest)
    {
        dest.Clear();
        lock (_lock)
        {
            for (var i = 0; i < _count; i++) dest.Add(_buf[i]);
            _count = 0;
        }
    }

    /// <summary>Discards any queued messages.</summary>
    public void Clear()
    {
        lock (_lock) _count = 0;
    }
}
