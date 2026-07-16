using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Persistence.Import;

/// <summary>
/// Decodes sample files referenced by an imported project after the arrangement is already on screen.
/// Import itself only stores paths so Open stays fast (FL-sized demos otherwise spend minutes in ffmpeg).
/// </summary>
public static class ImportAudioHydrator
{
    /// <summary>
    /// Loads pending clip/instrument samples. Returns how many unique paths were decoded successfully.
    /// </summary>
    public static int Hydrate(
        Project project,
        IAudioFileService audioFiles,
        CancellationToken cancellationToken = default,
        Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(audioFiles);

        var pathUsers = new Dictionary<string, List<Action<LoadedAudio>>>(StringComparer.OrdinalIgnoreCase);

        void Enqueue(string? path, Action<LoadedAudio> apply)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!pathUsers.TryGetValue(path, out var list))
            {
                list = new List<Action<LoadedAudio>>();
                pathUsers[path] = list;
            }

            list.Add(apply);
        }

        foreach (var track in project.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (!clip.IsAudio || clip.Samples is not null) continue;
                var path = clip.AudioFilePath;
                if (string.IsNullOrEmpty(path)) continue;
                var c = clip;
                Enqueue(path, loaded =>
                {
                    c.Samples = loaded.Samples;
                    c.Waveform = loaded.Waveform;
                    if (loaded.Tempo is { } tempo)
                        c.SourceTempo = tempo;
                });
            }

            foreach (var slot in track.Instruments)
            {
                if (slot.Instrument is not BasicSamplerInstrument sampler) continue;
                if (sampler.CurrentSample is not null) continue;
                var path = sampler.SampleFilePath;
                if (string.IsNullOrEmpty(path)) continue;
                var s = sampler;
                Enqueue(path, loaded =>
                    s.LoadSample(loaded.Samples, Path.GetFileName(path)));
            }
        }

        if (pathUsers.Count == 0) return 0;

        var paths = new List<string>(pathUsers.Keys);
        var decoded = new ConcurrentDictionary<string, LoadedAudio?>(StringComparer.OrdinalIgnoreCase);
        var done = 0;
        var total = paths.Count;

        Parallel.ForEach(
            paths,
            new ParallelOptions
            {
                // Device is stopped during import hydrate — use a few workers for speed.
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 4),
                CancellationToken = cancellationToken
            },
            path =>
            {
                LoadedAudio? loaded = null;
                try
                {
                    if (File.Exists(path))
                        loaded = audioFiles.Load(path, analyzeTempo: false);
                }
                catch
                {
                    loaded = null;
                }

                decoded[path] = loaded;
                var n = Interlocked.Increment(ref done);
                progress?.Invoke(n, total);
            });

        var ok = 0;
        foreach (var (path, appliers) in pathUsers)
        {
            if (!decoded.TryGetValue(path, out var loaded) || loaded is null) continue;
            ok++;
            foreach (var apply in appliers)
                apply(loaded);
        }

        return ok;
    }
}
