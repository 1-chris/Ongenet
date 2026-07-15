using System;

namespace Ongenet.App.Services;

/// <summary>Default no-op sampler for heads that do not expose process metrics.</summary>
public sealed class NullSystemMetricsSampler : ISystemMetricsSampler
{
    public bool IsAvailable => false;
    public double? CpuPercent => null;
    public long MemoryBytes => 0;
    public long ManagedHeapBytes => 0;
    public double? AudioLoadPercent => null;
    public long UnderrunCount => 0;
    public event Action? Updated { add { } remove { } }

    public void Start() { }
}
