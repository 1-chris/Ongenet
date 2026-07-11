using System;

namespace Ongenet.App.Services;

/// <summary>Default no-op sampler for heads that do not expose process metrics.</summary>
public sealed class NullSystemMetricsSampler : ISystemMetricsSampler
{
    public bool IsAvailable => false;
    public double? CpuPercent => null;
    public long MemoryBytes => 0;
#pragma warning disable CS0067
    public event Action? Updated;
#pragma warning restore CS0067

    public void Start() { }
}
