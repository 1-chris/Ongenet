using System;

namespace Ongenet.App.Services;

/// <summary>
/// Samples process CPU and memory for the transport-bar indicators. The desktop head provides a real
/// implementation; other heads register <see cref="NullSystemMetricsSampler"/>.
/// </summary>
public interface ISystemMetricsSampler
{
    /// <summary>True when this head can sample process metrics (desktop only).</summary>
    bool IsAvailable { get; }

    /// <summary>Process CPU usage, 0..100, or null before the first complete sample.</summary>
    double? CpuPercent { get; }

    /// <summary>Process working-set size in bytes.</summary>
    long MemoryBytes { get; }

    /// <summary>Managed GC heap size in bytes (0 when unavailable).</summary>
    long ManagedHeapBytes { get; }

    /// <summary>
    /// Last audio block as a percentage of its deadline (100 = at budget). Null when no blocks yet.
    /// </summary>
    double? AudioLoadPercent { get; }

    /// <summary>Cumulative CoreAudio/device underrun count from <see cref="Core.Audio.AudioDiagnostics"/>.</summary>
    long UnderrunCount { get; }

    /// <summary>Raised on the UI thread after each sample.</summary>
    event Action? Updated;

    /// <summary>Starts periodic sampling. Safe to call more than once.</summary>
    void Start();
}
