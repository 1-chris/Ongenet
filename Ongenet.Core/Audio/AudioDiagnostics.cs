using System;
using System.Threading;

namespace Ongenet.Core.Audio;

/// <summary>
/// Lock-free counters updated by the audio path for profiling block render time and device underruns.
/// Read from the UI thread via <see cref="Snapshot"/>.
/// </summary>
public static class AudioDiagnostics
{
    private const int RecentCapacity = 1024;
    private const long DefaultBudgetMicros = 10_667; // 512 frames @ 48 kHz

    private static long _blockCount;
    private static long _blockTimeTotalMicros;
    private static long _blockTimeMaxMicros;
    private static long _underrunCount;
    private static long _ringFillFrames;
    private static long _lastBlockMicros;
    private static long _overBudgetCount;
    private static long _blockBudgetMicros = DefaultBudgetMicros;
    private static long _lastRenderMicros;
    private static long _lastMixdownMicros;
    private static long _lastRenderAllocatedBytes;
    private static long _lastMixdownAllocatedBytes;
    private static long _totalAudioThreadAllocatedBytes;
    private static long _maxTrackMicros;
    private static long _maxTrackAllocatedBytes;
    private static string? _maxTimeTrackName;
    private static string? _maxAllocationTrackName;
    private static long _recentWrite;
    private static int _recentCount;
    private static readonly long[] Recent = new long[RecentCapacity];

    /// <summary>Most recent block render duration in microseconds.</summary>
    public static long LastBlockMicroseconds => Volatile.Read(ref _lastBlockMicros);

    /// <summary>Sets the per-block deadline used for over-budget counting (frames / sampleRate).</summary>
    public static void SetBlockBudget(int frames, int sampleRate)
    {
        if (frames < 1 || sampleRate < 1) return;
        // Keep the largest observed quantum — trailing partial pumps must not shrink the budget.
        var micros = Math.Max(1, frames * 1_000_000L / sampleRate);
        while (true)
        {
            var current = Volatile.Read(ref _blockBudgetMicros);
            if (micros <= current) break;
            if (Interlocked.CompareExchange(ref _blockBudgetMicros, micros, current) == current) break;
        }
    }

    public static AudioDiagnosticsSnapshot Snapshot()
    {
        var count = Volatile.Read(ref _blockCount);
        var total = Volatile.Read(ref _blockTimeTotalMicros);
        var max = Volatile.Read(ref _blockTimeMaxMicros);
        ComputePercentiles(out var p95, out var p99);
        return new AudioDiagnosticsSnapshot(
            count,
            count > 0 ? total / count : 0,
            max,
            Volatile.Read(ref _underrunCount),
            Volatile.Read(ref _ringFillFrames),
            Volatile.Read(ref _lastBlockMicros),
            Volatile.Read(ref _overBudgetCount),
            Volatile.Read(ref _blockBudgetMicros),
            p95,
            p99,
            Volatile.Read(ref _lastRenderMicros),
            Volatile.Read(ref _lastMixdownMicros),
            Volatile.Read(ref _lastRenderAllocatedBytes),
            Volatile.Read(ref _lastMixdownAllocatedBytes),
            Volatile.Read(ref _totalAudioThreadAllocatedBytes),
            Volatile.Read(ref _maxTrackMicros),
            Volatile.Read(ref _maxTrackAllocatedBytes),
            Volatile.Read(ref _maxTimeTrackName),
            Volatile.Read(ref _maxAllocationTrackName));
    }

    public static void RecordBlock(long microseconds)
    {
        Volatile.Write(ref _lastBlockMicros, microseconds);
        Interlocked.Increment(ref _blockCount);
        Interlocked.Add(ref _blockTimeTotalMicros, microseconds);

        var budget = Volatile.Read(ref _blockBudgetMicros);
        if (microseconds > budget)
            Interlocked.Increment(ref _overBudgetCount);

        var idx = (int)(Interlocked.Increment(ref _recentWrite) - 1) & (RecentCapacity - 1);
        Recent[idx] = microseconds;
        var filled = Volatile.Read(ref _recentCount);
        if (filled < RecentCapacity)
            Interlocked.CompareExchange(ref _recentCount, filled + 1, filled);

        while (true)
        {
            var current = Volatile.Read(ref _blockTimeMaxMicros);
            if (microseconds <= current) break;
            if (Interlocked.CompareExchange(ref _blockTimeMaxMicros, microseconds, current) == current) break;
        }
    }

    public static void RecordPhases(long renderMicros, long mixdownMicros)
    {
        Volatile.Write(ref _lastRenderMicros, renderMicros);
        Volatile.Write(ref _lastMixdownMicros, mixdownMicros);
    }

    public static void RecordAllocations(long renderBytes, long mixdownBytes)
    {
        Volatile.Write(ref _lastRenderAllocatedBytes, Math.Max(0, renderBytes));
        Volatile.Write(ref _lastMixdownAllocatedBytes, Math.Max(0, mixdownBytes));
        Interlocked.Add(ref _totalAudioThreadAllocatedBytes, Math.Max(0, renderBytes) + Math.Max(0, mixdownBytes));
    }

    public static void RecordTrack(string name, long microseconds, long allocatedBytes)
    {
        UpdateMaximum(ref _maxTrackMicros, microseconds, name, ref _maxTimeTrackName);
        UpdateMaximum(ref _maxTrackAllocatedBytes, allocatedBytes, name, ref _maxAllocationTrackName);
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
        Interlocked.Exchange(ref _overBudgetCount, 0);
        Volatile.Write(ref _lastRenderMicros, 0);
        Volatile.Write(ref _lastMixdownMicros, 0);
        Volatile.Write(ref _lastRenderAllocatedBytes, 0);
        Volatile.Write(ref _lastMixdownAllocatedBytes, 0);
        Interlocked.Exchange(ref _totalAudioThreadAllocatedBytes, 0);
        Interlocked.Exchange(ref _maxTrackMicros, 0);
        Interlocked.Exchange(ref _maxTrackAllocatedBytes, 0);
        Volatile.Write(ref _maxTimeTrackName, null);
        Volatile.Write(ref _maxAllocationTrackName, null);
        Interlocked.Exchange(ref _recentWrite, 0);
        Volatile.Write(ref _recentCount, 0);
        Array.Clear(Recent);
    }

    private static void ComputePercentiles(out long p95, out long p99)
    {
        var count = Volatile.Read(ref _recentCount);
        if (count <= 0)
        {
            p95 = p99 = 0;
            return;
        }

        var copy = new long[count];
        // Approximate recent window: copy what we can without locking.
        var write = Volatile.Read(ref _recentWrite);
        for (var i = 0; i < count; i++)
            copy[i] = Recent[(write - count + i) & (RecentCapacity - 1)];

        Array.Sort(copy);
        p95 = copy[Math.Clamp((int)(count * 0.95), 0, count - 1)];
        p99 = copy[Math.Clamp((int)(count * 0.99), 0, count - 1)];
    }

    private static void UpdateMaximum(ref long target, long value, string name, ref string? targetName)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (value <= current) return;
            if (Interlocked.CompareExchange(ref target, value, current) != current) continue;
            Volatile.Write(ref targetName, name);
            return;
        }
    }
}

public readonly record struct AudioDiagnosticsSnapshot(
    long BlockCount,
    long AverageBlockMicroseconds,
    long MaxBlockMicroseconds,
    long UnderrunCount,
    long RingFillFrames,
    long LastBlockMicroseconds,
    long OverBudgetCount = 0,
    long BlockBudgetMicroseconds = 0,
    long P95BlockMicroseconds = 0,
    long P99BlockMicroseconds = 0,
    long LastRenderMicroseconds = 0,
    long LastMixdownMicroseconds = 0,
    long LastRenderAllocatedBytes = 0,
    long LastMixdownAllocatedBytes = 0,
    long TotalAudioThreadAllocatedBytes = 0,
    long MaxTrackMicroseconds = 0,
    long MaxTrackAllocatedBytes = 0,
    string? MaxTimeTrackName = null,
    string? MaxAllocationTrackName = null)
{
    /// <summary>Fraction of the block budget used by the last block (1.0 = deadline).</summary>
    public double LastLoad =>
        BlockBudgetMicroseconds > 0 ? LastBlockMicroseconds / (double)BlockBudgetMicroseconds : 0;

    /// <summary>Fraction of blocks that exceeded the render deadline.</summary>
    public double OverBudgetRatio =>
        BlockCount > 0 ? OverBudgetCount / (double)BlockCount : 0;
}
