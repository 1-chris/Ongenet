using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ongenet.Core.Services.Interfaces;

public interface IVideoProxyCacheService
{
    bool IsAvailable { get; }
    string? GetProxyPath(string sourcePath, string? projectDirectory);
    Task<string?> EnsureProxyAsync(string sourcePath, string? projectDirectory, CancellationToken ct = default);
}

public sealed class VideoRenderJobInfo
{
    public Guid Id { get; init; }
    public required string OutputPath { get; init; }
    public double Progress { get; set; }
    public string Status { get; set; } = "Queued";
    public bool IsComplete { get; set; }
    public bool IsFailed { get; set; }
    public string? Error { get; set; }
}

public interface IVideoRenderQueueService
{
    IReadOnlyCollection<VideoRenderJobInfo> Jobs { get; }
    event Action? JobsChanged;
    VideoRenderJobInfo Enqueue(string outputPath, double regionStartBeat, double regionEndBeat);
    void Cancel(Guid jobId);
}
