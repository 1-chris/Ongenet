using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.VideoComposition.Services;

/// <summary>Background ffmpeg H.264 proxy transcode cache beside the project.</summary>
public sealed class VideoProxyCacheService : IVideoProxyCacheService
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAvailable => FfmpegEncoder.IsAvailable;

    public string? GetProxyPath(string sourcePath, string? projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !System.IO.File.Exists(sourcePath)) return null;
        var proxy = BuildProxyPath(sourcePath, projectDirectory);
        return System.IO.File.Exists(proxy) ? proxy : _cache.TryGetValue(sourcePath, out var cached) ? cached : null;
    }

    public Task<string?> EnsureProxyAsync(string sourcePath, string? projectDirectory, CancellationToken ct = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(sourcePath) || !System.IO.File.Exists(sourcePath))
            return Task.FromResult<string?>(null);

        var existing = GetProxyPath(sourcePath, projectDirectory);
        if (existing is not null) return Task.FromResult<string?>(existing);

        return _inFlight.GetOrAdd(sourcePath, _ => TranscodeAsync(sourcePath, projectDirectory, ct));
    }

    private async Task<string?> TranscodeAsync(string sourcePath, string? projectDirectory, CancellationToken ct)
    {
        try
        {
            var ffmpeg = FfmpegEncoder.Locate();
            if (ffmpeg is null) return null;

            var proxy = BuildProxyPath(sourcePath, projectDirectory);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(proxy)!);
            if (System.IO.File.Exists(proxy)) return proxy;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(sourcePath);
            psi.ArgumentList.Add("-c:v");
            psi.ArgumentList.Add("libx264");
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add("veryfast");
            psi.ArgumentList.Add("-crf");
            psi.ArgumentList.Add("23");
            psi.ArgumentList.Add("-an");
            psi.ArgumentList.Add(proxy);

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (proc.ExitCode != 0 || !System.IO.File.Exists(proxy)) return null;

            _cache[sourcePath] = proxy;
            return proxy;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inFlight.TryRemove(sourcePath, out _);
        }
    }

    private static string BuildProxyPath(string sourcePath, string? projectDirectory)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sourcePath)))[..16];
        var baseDir = !string.IsNullOrWhiteSpace(projectDirectory)
            ? System.IO.Path.Combine(projectDirectory, ".ongenet", "video-proxies")
            : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ongenet-video-proxies");
        return System.IO.Path.Combine(baseDir, $"{hash}.mp4");
    }
}
