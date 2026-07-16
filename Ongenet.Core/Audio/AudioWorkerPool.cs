using System;
using System.Threading;

namespace Ongenet.Core.Audio;

/// <summary>
/// A fixed pool of high-priority worker threads for fanning the per-track render work of one audio
/// block across CPU cores — the difference between one core doing a 24-track mix and all of them.
///
/// Designed for the real-time audio path: no allocation per block, no thread-pool scheduling jitter,
/// and the calling (audio) thread participates in the work instead of blocking idle. Jobs are claimed
/// with a single interlocked increment over the job count; completion is signalled with a hybrid
/// spin-then-event wait so the common case (workers finish within microseconds of each other) never
/// touches the kernel. Workers sleep on an event between blocks, so an idle pool costs nothing.
/// </summary>
public sealed class AudioWorkerPool : IDisposable
{
    private readonly Thread[] _workers;
    private readonly ManualResetEventSlim _work = new(false);
    private readonly ManualResetEventSlim _done = new(false);

    private Action<int>? _job;
    private int _jobCount;
    private int _nextJob;
    private int _pending;      // jobs not yet completed this dispatch
    private int _generation;   // bumped per dispatch so workers never re-run a stale batch
    private volatile bool _disposed;

    /// <summary>Worker threads in addition to the calling audio thread.</summary>
    public int WorkerCount { get; }

    public AudioWorkerPool(int? workerCount = null)
    {
        // The audio callback participates, so three workers provide four render lanes. On hybrid
        // Apple Silicon, waking every efficiency core increased Ascension's average block time by
        // 60–70% and produced long scheduler-tail spikes. Keep the realtime batch on a small bounded
        // set of high-priority lanes; callers can still request a different count for benchmarks.
        WorkerCount = Math.Clamp(workerCount ?? Math.Min(Environment.ProcessorCount - 1, 3), 0, 12);
        _workers = new Thread[WorkerCount];
        for (var i = 0; i < WorkerCount; i++)
        {
            _workers[i] = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = $"Ongenet Audio Worker {i}"
            };
            _workers[i].Start();
        }
    }

    /// <summary>
    /// Runs <paramref name="job"/> for indices 0..<paramref name="jobCount"/>-1 across the pool and
    /// the calling thread, returning when every job has finished. Falls back to inline execution for
    /// tiny batches (or when there are no workers) where fan-out overhead would exceed the work.
    /// </summary>
    public void Run(int jobCount, Action<int> job)
    {
        if (jobCount <= 0) return;
        if (WorkerCount == 0 || jobCount == 1 || _disposed)
        {
            for (var i = 0; i < jobCount; i++) job(i);
            return;
        }

        _job = job;
        _jobCount = jobCount;
        _nextJob = 0;
        _pending = jobCount;
        Interlocked.Increment(ref _generation);
        _done.Reset();
        _work.Set(); // release the workers

        // The audio thread works the same queue rather than waiting idle.
        DrainJobs();

        // Wait for stragglers: spin briefly (the usual case — everyone finishes together), then block.
        var spin = new SpinWait();
        while (Volatile.Read(ref _pending) > 0)
        {
            if (spin.NextSpinWillYield)
            {
                if (_done.Wait(2)) break;
            }
            else
            {
                spin.SpinOnce();
            }
        }

        _work.Reset();
        _job = null;
    }

    private void WorkerLoop()
    {
        var seenGeneration = 0;
        while (!_disposed)
        {
            _work.Wait();
            if (_disposed) return;

            // Only join a batch we haven't drained already (the event may still be set momentarily).
            var gen = Volatile.Read(ref _generation);
            if (gen == seenGeneration)
            {
                Thread.Yield();
                continue;
            }

            seenGeneration = gen;
            DrainJobs();
        }
    }

    private void DrainJobs()
    {
        var job = _job;
        if (job is null) return;
        // Snapshot count so a concurrent Run() cannot stretch indices past the caller's array.
        var jobCount = Volatile.Read(ref _jobCount);
        while (true)
        {
            var index = Interlocked.Increment(ref _nextJob) - 1;
            if (index >= jobCount) return;
            try
            {
                job(index);
            }
            finally
            {
                if (Interlocked.Decrement(ref _pending) == 0) _done.Set();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _work.Set(); // wake workers so they observe _disposed and exit
        foreach (var worker in _workers)
        {
            if (!worker.Join(200)) { /* background thread — the process can exit regardless */ }
        }

        _work.Dispose();
        _done.Dispose();
    }
}
