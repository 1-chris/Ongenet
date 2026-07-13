using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.VideoComposition.Services;

/// <summary>Background video export queue reusing <see cref="ExportService"/>.</summary>
public sealed class VideoRenderQueueService : IVideoRenderQueueService
{
    private readonly ExportService _export;
    private readonly IProjectService _project;
    private readonly ConcurrentDictionary<Guid, VideoRenderJobInfo> _jobs = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public VideoRenderQueueService(ExportService export, IProjectService project)
    {
        _export = export;
        _project = project;
    }

    public IReadOnlyCollection<VideoRenderJobInfo> Jobs => _jobs.Values.ToArray();

    public event Action? JobsChanged;

    public VideoRenderJobInfo Enqueue(string outputPath, double regionStartBeat, double regionEndBeat)
    {
        var job = new VideoRenderJobInfo
        {
            Id = Guid.NewGuid(),
            OutputPath = outputPath
        };
        _jobs[job.Id] = job;
        JobsChanged?.Invoke();
        _ = ProcessQueueAsync(job, regionStartBeat, regionEndBeat);
        return job;
    }

    public void Cancel(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job) && !job.IsComplete)
        {
            job.Status = "Cancelled";
            job.IsFailed = true;
            JobsChanged?.Invoke();
        }
    }

    private async Task ProcessQueueAsync(VideoRenderJobInfo job, double regionStartBeat, double regionEndBeat)
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (job.IsFailed) return;
            job.Status = "Rendering";
            JobsChanged?.Invoke();
            try
            {
                var progress = new Progress<double>(p =>
                {
                    job.Progress = p;
                    JobsChanged?.Invoke();
                });
                await _export.ExportCompositedVideoAsync(
                    _project.Current, job.OutputPath, regionStartBeat, regionEndBeat, progress)
                    .ConfigureAwait(false);
                job.Progress = 1;
                job.Status = "Complete";
                job.IsComplete = true;
            }
            catch (Exception ex)
            {
                job.Status = "Failed";
                job.Error = ex.Message;
                job.IsFailed = true;
            }

            JobsChanged?.Invoke();
        }
        finally
        {
            _gate.Release();
        }
    }
}
