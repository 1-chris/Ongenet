using System.Threading;

namespace Ongenet.Core.Audio;

/// <summary>
/// Lock-free counters updated by the audio path for profiling block render time and device underruns.
/// Read from the UI thread via <see cref="Snapshot"/>.
/// </summary>
public static class AudioDiagnostics
{
    private static long _blockCount;
    private static long _blockTimeTotalMicros;
    private static long _blockTimeMaxMicros;
    private static long _underrunCount;
    private static long _ringFillFrames;

    /// <summary>Most recent block render duration in microseconds.</summary>
    public static long LastBlockMicroseconds => Volatile.Read(ref _lastBlockMicros);
    private static long _lastBlockMicros;

    public static AudioDiagnosticsSnapshot Snapshot()
    {
        var count = Volatile.Read(ref _blockCount);
        var total = Volatile.Read(ref _blockTimeTotalMicros);
        var max = Volatile.Read(ref _blockTimeMaxMicros);
        return new AudioDiagnosticsSnapshot(
            count,
            count > 0 ? total / count : 0,
            max,
            Volatile.Read(ref _underrunCount),
            Volatile.Read(ref _ringFillFrames),
            Volatile.Read(ref _lastBlockMicros));
    }

    public static void RecordBlock(long microseconds)
    {
        Volatile.Write(ref _lastBlockMicros, microseconds);
        Interlocked.Increment(ref _blockCount);
        Interlocked.Add(ref _blockTimeTotalMicros, microseconds);

        while (true)
        {
            var current = Volatile.Read(ref _blockTimeMaxMicros);
            if (microseconds <= current) break;
            if (Interlocked.CompareExchange(ref _blockTimeMaxMicros, microseconds, current) == current) break;
        }
    }

    public static void RecordUnderrun() => Interlocked.Increment(ref _underrunCount);

    public static void RecordRingFill(long frames) => Volatile.Write(ref _ringFillFrames, frames);

    public static void Reset()
    {
        Interlocked.Exchange(ref _blockCount, 0);
        Interlocked.Exchange(ref _blockTimeTotalMicros, 0);
        Interlocked.Exchange(ref _blockTimeMaxMicros, 0);
        Interlocked.Exchange(ref _underrunCount, 0);
        Volatile.Write(ref _ringFillFrames, 0);
        Volatile.Write(ref _lastBlockMicros, 0);
    }
}

public readonly record struct AudioDiagnosticsSnapshot(
    long BlockCount,
    long AverageBlockMicroseconds,
    long MaxBlockMicroseconds,
    long UnderrunCount,
    long RingFillFrames,
    long LastBlockMicroseconds);
