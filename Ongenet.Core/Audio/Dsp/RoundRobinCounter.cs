using System.Collections.Generic;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Tracks per-group sequence positions for round-robin sample selection (SFZ <c>seq_length</c> /
/// <c>seq_position</c>), so repeated notes cycle through alternate samples instead of retriggering the
/// same one — the classic fix for the "machine-gun" effect. Reusable by any instrument that wants
/// alternating layers. Not thread-safe; advance it under the instrument's note lock.
/// </summary>
public sealed class RoundRobinCounter
{
    private readonly Dictionary<int, int> _counters = new();

    /// <summary>
    /// Returns the current 1-based position in [1, <paramref name="length"/>] for
    /// <paramref name="groupKey"/>, then advances that group's counter by one.
    /// </summary>
    public int NextPosition(int groupKey, int length)
    {
        if (length < 1) length = 1;
        _counters.TryGetValue(groupKey, out var count);
        _counters[groupKey] = count + 1;
        return count % length + 1;
    }

    /// <summary>Resets all sequence positions (e.g. on transport stop or instrument reload).</summary>
    public void Reset() => _counters.Clear();
}
