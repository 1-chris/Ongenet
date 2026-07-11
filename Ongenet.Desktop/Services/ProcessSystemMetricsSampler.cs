using System;
using System.Diagnostics;
using Avalonia.Threading;
using Ongenet.App.Services;

namespace Ongenet.Desktop.Services;

/// <summary>
/// Samples the current process CPU % and working-set RAM once per second for the transport-bar indicators.
/// </summary>
public sealed class ProcessSystemMetricsSampler : ISystemMetricsSampler
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TimeSpan _lastCpuTime;
    private long _lastWallMs;
    private bool _started;

    public bool IsAvailable => true;

    public double? CpuPercent { get; private set; }

    public long MemoryBytes { get; private set; }

    public event Action? Updated;

    public ProcessSystemMetricsSampler()
    {
        _timer.Tick += (_, _) => Sample();
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _lastCpuTime = _process.TotalProcessorTime;
        _lastWallMs = Environment.TickCount64;
        _timer.Start();
        Sample();
    }

    private void Sample()
    {
        _process.Refresh();
        MemoryBytes = _process.WorkingSet64;

        var nowWallMs = Environment.TickCount64;
        var nowCpu = _process.TotalProcessorTime;
        var wallDeltaMs = nowWallMs - _lastWallMs;
        if (wallDeltaMs > 0)
        {
            var cpuDeltaMs = (nowCpu - _lastCpuTime).TotalMilliseconds;
            var cores = Math.Max(1, Environment.ProcessorCount);
            var pct = cpuDeltaMs / wallDeltaMs / cores * 100.0;
            CpuPercent = Math.Clamp(pct, 0, 100);
        }

        _lastCpuTime = nowCpu;
        _lastWallMs = nowWallMs;
        Updated?.Invoke();
    }
}
