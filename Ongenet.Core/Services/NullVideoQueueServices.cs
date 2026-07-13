using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services;

public sealed class NullVideoProxyCacheService : IVideoProxyCacheService
{
    public bool IsAvailable => false;
    public string? GetProxyPath(string sourcePath, string? projectDirectory) => null;
    public Task<string?> EnsureProxyAsync(string sourcePath, string? projectDirectory, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}

public sealed class NullVideoRenderQueueService : IVideoRenderQueueService
{
    public IReadOnlyCollection<VideoRenderJobInfo> Jobs { get; } = Array.Empty<VideoRenderJobInfo>();
    public event Action? JobsChanged;
    public VideoRenderJobInfo Enqueue(string outputPath, double regionStartBeat, double regionEndBeat) =>
        throw new NotSupportedException("Video render queue is not available on this platform.");
    public void Cancel(Guid jobId) { }
}
