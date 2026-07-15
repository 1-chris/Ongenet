using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services;

/// <summary>Offline export options for master, stems, batch, and region renders.</summary>
public sealed class ExportOptions
{
    public ExportKind Kind { get; set; } = ExportKind.Master;
    public int BitDepth { get; set; } = 16;
    public double RegionStartBeat { get; set; }
    public double RegionEndBeat { get; set; }
    public IReadOnlyList<Guid>? TrackIds { get; set; }
    public bool IncludeMasterFx { get; set; } = true;
    /// <summary>When true on Master/Region/Batch, Master inserts are bypassed (pre-master bounce).</summary>
    public bool BypassMasterFx { get; set; }
    public bool ApplyDither { get; set; }
    public DitherMode DitherMode { get; set; } = DitherMode.Tpdf;
    public bool AnalyzeLoudness { get; set; } = true;
    public bool NormalizeLoudness { get; set; }
    public bool MatchAlbumLoudness { get; set; }
    public double TargetIntegratedLufs { get; set; } = -14.0;
    public double TargetTruePeakDbTp { get; set; } = -1.0;
    public string? DeliveryPlatform { get; set; }
    public SurroundFormat Surround { get; set; } = SurroundFormat.Stereo;
    public ExportAudioFormat AudioFormat { get; set; } = ExportAudioFormat.Wav;
    public bool MuxWithVideo { get; set; }
    public Guid? VideoTrackId { get; set; }
    public bool ComposeVideo { get; set; }

    /// <summary>When true on Master/Region, also write a 30s (or region) pre-master vs mastered WAV pair.</summary>
    public bool ExportComparisonPair { get; set; }

    /// <summary>Also write a JSON loudness report beside the text sidecar.</summary>
    public bool WriteLoudnessJson { get; set; } = true;

    /// <summary>Optional destination sample rate (Hz). 0 = keep project/engine rate. Typical: 44100 for CD.</summary>
    public int TargetSampleRate { get; set; }

    /// <summary>Filled after export when <see cref="AnalyzeLoudness"/> or normalize measured loudness.</summary>
    public LoudnessReport? LoudnessReport { get; set; }
}

public static class DeliveryPlatformPresets
{
    public static readonly (string Name, double Lufs, double DbTp)[] All =
    {
        ("Spotify", -14.0, -1.0),
        ("YouTube", -14.0, -1.0),
        ("Apple Music", -16.0, -1.0),
        ("Club", -9.0, -0.3),
        ("Podcast", -16.0, -1.5),
    };

    public static (double Lufs, double DbTp)? TryGet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var p in All)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return (p.Lufs, p.DbTp);
        return null;
    }

    public static string FormatLabel(string name, double lufs, double dbTp) =>
        $"{name} ({lufs:0.#} LUFS / {dbTp:0.#} dBTP)";
}

public enum ExportKind
{
    Master,
    Stems,
    Batch,
    Region
}

public enum SurroundFormat
{
    Stereo,
    Surround51,
    Surround71
}

/// <summary>Dry-run loudness measurement for one stem before album delivery.</summary>
public readonly record struct StemLoudnessAnalysis(string TrackName, LoudnessReport Report, double OffsetDb);

public enum ExportAudioFormat
{
    Wav,
    Flac,
    Mp3,
    Ogg
}

public static class ExportAudioFormatExtensions
{
    public static string GetExtension(this ExportAudioFormat format) => format switch
    {
        ExportAudioFormat.Flac => "flac",
        ExportAudioFormat.Mp3 => "mp3",
        ExportAudioFormat.Ogg => "ogg",
        _ => "wav"
    };

    public static string GetDescription(this ExportAudioFormat format) => format switch
    {
        ExportAudioFormat.Flac => "FLAC audio",
        ExportAudioFormat.Mp3 => "MP3 audio",
        ExportAudioFormat.Ogg => "OGG Vorbis audio",
        _ => "WAV audio"
    };
}

/// <summary>Batch and stem export built on <see cref="OfflineRenderer"/>.</summary>
public sealed class ExportService
{
    private readonly OfflineRenderer _renderer = new();
    private readonly IVideoCompositor _videoCompositor;
    private readonly IVideoMuxer _videoMuxer;

    public ExportService(IVideoCompositor videoCompositor, IVideoMuxer videoMuxer)
    {
        _videoCompositor = videoCompositor;
        _videoMuxer = videoMuxer;
    }

    /// <summary>
    /// Renders selected stems to memory and measures them without writing deliverables.
    /// Album offsets use the same relative-loudness calculation as the final stem export.
    /// </summary>
    public IReadOnlyList<StemLoudnessAnalysis> AnalyzeStemLoudness(Project project, AudioFormat format,
        double bpm, IReadOnlyList<Guid>? trackIds, bool includeMasterFx, SurroundFormat surround,
        int targetSampleRate, double targetLufs, double targetTruePeakDbTp,
        IProgress<double>? progress = null)
    {
        var ids = trackIds?.ToHashSet();
        var targets = project.Tracks
            .Where(t => !t.IsBus && (ids is null || ids.Contains(t.Id)))
            .ToList();
        var reports = new LoudnessReport[targets.Count];
        for (var i = 0; i < targets.Count; i++)
        {
            var stemProject = CloneProjectForTrack(project, targets[i], includeMasterFx);
            var index = i;
            var sub = progress is null ? null : new Progress<double>(f =>
                progress.Report((index + f) / Math.Max(1, targets.Count)));
            var buffer = new OfflineRenderer().RenderMasterToBuffer(stemProject, format, bpm, sub,
                surround: surround);
            if (targetSampleRate > 0 && targetSampleRate != buffer.SampleRate)
                buffer = ResampleBuffer(buffer, targetSampleRate);
            reports[i] = LoudnessAnalyzer.Analyze(buffer.Samples,
                new AudioFormat(buffer.SampleRate, buffer.Channels), targetLufs, targetTruePeakDbTp);
        }

        var offsets = ComputeAlbumOffsets(reports.Select(r => (double)r.IntegratedLufs).ToArray(), targetLufs);
        return targets.Select((track, i) => new StemLoudnessAnalysis(track.Name, reports[i], offsets[i])).ToArray();
    }

    public void ExportAdmBwf(Project project, AudioFormat format, double bpm, string outputPath,
        ExportOptions options, IProgress<double>? progress = null)
    {
        var regionStart = options.Kind == ExportKind.Region ? (double?)options.RegionStartBeat : null;
        var regionEnd = options.Kind == ExportKind.Region ? (double?)options.RegionEndBeat : null;
        var buffer = _renderer.RenderMasterToBuffer(project, format, bpm, progress, regionStart, regionEnd,
            options.Surround);
        var path = outputPath.EndsWith(AdmBwfExporter.DefaultExtension, StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : outputPath + AdmBwfExporter.DefaultExtension;
        AdmBwfExporter.Export(project, path, buffer.Samples.AsSpan(), buffer.Channels, buffer.SampleRate, bpm);

        if (options.AnalyzeLoudness)
        {
            var fmt = new AudioFormat(buffer.SampleRate, buffer.Channels);
            options.LoudnessReport = LoudnessAnalyzer.Analyze(buffer.Samples, fmt,
                options.TargetIntegratedLufs, options.TargetTruePeakDbTp);
            WriteLoudnessSidecars(path, options.LoudnessReport.Value, options.WriteLoudnessJson,
                options.TargetIntegratedLufs);
        }
    }

    public void Export(Project project, AudioFormat format, double bpm, string outputPath,
        ExportOptions options, IProgress<double>? progress = null,
        IVideoWaveformCacheService? waveformCache = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var regionStart = options.Kind == ExportKind.Region ? (double?)options.RegionStartBeat : null;
        var regionEnd = options.Kind == ExportKind.Region ? (double?)options.RegionEndBeat : null;
        LoudnessReport? pendingReport = null;

        void RenderWav(string wavPath)
        {
            switch (options.Kind)
            {
                case ExportKind.Master:
                case ExportKind.Region:
                {
                    ApplyDeliveryPlatform(options);
                    var loudness = options.AnalyzeLoudness || options.NormalizeLoudness
                        ? new LoudnessAnalyzer()
                        : null;
                    if (options.NormalizeLoudness)
                    {
                        // Two-pass: render → measure → gain → re-limit (boost) or TP ceiling (cut) → write.
                        var buf = new OfflineRenderer().RenderMasterToBuffer(project, format, bpm, progress,
                            regionStart, regionEnd, options.Surround, options.BypassMasterFx);
                        var fmt = new AudioFormat(buf.SampleRate, buf.Channels);
                        var report = LoudnessAnalyzer.Analyze(buf.Samples, fmt,
                            options.TargetIntegratedLufs, options.TargetTruePeakDbTp);
                        if (!float.IsNegativeInfinity(report.IntegratedLufs))
                        {
                            var gainDb = options.TargetIntegratedLufs - report.IntegratedLufs;
                            var gain = (float)Math.Pow(10.0, gainDb / 20.0);
                            for (var i = 0; i < buf.Samples.Length; i++)
                                buf.Samples[i] *= gain;

                            // Boosting after the master chain recreates overs — re-run Peak Limiter.
                            // Attenuation is safe with a linear TP ceiling only.
                            if (gainDb > 0.05)
                                ApplyDeliveryLimiter(buf.Samples, fmt, options.TargetTruePeakDbTp);
                            else
                                ApplyTruePeakCeiling(buf.Samples, fmt, options.TargetTruePeakDbTp);
                        }
                        else
                        {
                            ApplyTruePeakCeiling(buf.Samples, fmt, options.TargetTruePeakDbTp);
                        }

                        if (options.TargetSampleRate > 0 && options.TargetSampleRate != buf.SampleRate)
                        {
                            buf = ResampleBuffer(buf, options.TargetSampleRate);
                            fmt = new AudioFormat(buf.SampleRate, buf.Channels);
                        }

                        loudness = new LoudnessAnalyzer();
                        loudness.Prepare(fmt);
                        using (var writer = new WavWriter(wavPath, buf.Channels, buf.SampleRate, options.BitDepth,
                                   options.ApplyDither, options.DitherMode))
                        {
                            const int block = 4096;
                            for (var i = 0; i < buf.Samples.Length; i += block)
                            {
                                var len = Math.Min(block, buf.Samples.Length - i);
                                var slice = buf.Samples.AsSpan(i, len);
                                loudness.Process(slice);
                                writer.Write(slice);
                            }
                        }
                        options.LoudnessReport = loudness.Finish(options.TargetIntegratedLufs, options.TargetTruePeakDbTp);
                    }
                    else
                    {
                        if (options.TargetSampleRate > 0 && options.TargetSampleRate != format.SampleRate)
                        {
                            var buf = new OfflineRenderer().RenderMasterToBuffer(project, format, bpm, progress,
                                regionStart, regionEnd, options.Surround, options.BypassMasterFx);
                            buf = ResampleBuffer(buf, options.TargetSampleRate);
                            var fmt = new AudioFormat(buf.SampleRate, buf.Channels);
                            loudness ??= options.AnalyzeLoudness ? new LoudnessAnalyzer() : null;
                            if (loudness is not null) loudness.Prepare(fmt);
                            using var writer = new WavWriter(wavPath, buf.Channels, buf.SampleRate, options.BitDepth,
                                options.ApplyDither, options.DitherMode);
                            const int block = 4096;
                            for (var i = 0; i < buf.Samples.Length; i += block)
                            {
                                var len = Math.Min(block, buf.Samples.Length - i);
                                var slice = buf.Samples.AsSpan(i, len);
                                loudness?.Process(slice);
                                writer.Write(slice);
                            }
                            if (loudness is not null)
                                options.LoudnessReport = loudness.Finish(options.TargetIntegratedLufs, options.TargetTruePeakDbTp);
                        }
                        else
                        {
                            new OfflineRenderer().RenderToWav(project, format, bpm, wavPath, progress,
                                options.BitDepth, regionStart, regionEnd, options.Surround,
                                options.BypassMasterFx, options.ApplyDither, skipAnalysers: true, loudness,
                                options.DitherMode);
                            if (loudness is not null)
                                options.LoudnessReport = loudness.Finish(options.TargetIntegratedLufs, options.TargetTruePeakDbTp);
                        }
                    }

                    pendingReport = options.LoudnessReport;

                    if (options.ExportComparisonPair && options.Kind is ExportKind.Master or ExportKind.Region)
                        ExportComparisonPairFiles(project, format, bpm, wavPath, options, regionStart, regionEnd);
                    break;
                }
                case ExportKind.Stems:
                    ExportStems(project, format, bpm, Path.GetDirectoryName(wavPath)!, options, progress);
                    pendingReport = options.LoudnessReport;
                    return;
                case ExportKind.Batch:
                    ExportBatch(project, format, bpm, Path.GetDirectoryName(wavPath)!, options, progress);
                    pendingReport = options.LoudnessReport;
                    return;
            }
        }

        static void ApplyDeliveryPlatform(ExportOptions options)
        {
            if (DeliveryPlatformPresets.TryGet(options.DeliveryPlatform) is not { } p) return;
            options.TargetIntegratedLufs = p.Lufs;
            options.TargetTruePeakDbTp = p.DbTp;
        }

        if (options.AudioFormat == ExportAudioFormat.Wav && !options.MuxWithVideo
            && !(options.ComposeVideo && project.VideoLayers.Count > 0))
        {
            RenderWav(outputPath);
            if (options.Kind is ExportKind.Master or ExportKind.Region
                && options.AnalyzeLoudness && pendingReport is { } lr)
            {
                WriteLoudnessSidecars(outputPath, lr, options.WriteLoudnessJson, options.TargetIntegratedLufs);
                WavLoudnessMetadata.Append(outputPath, lr.IntegratedLufs, lr.TruePeakDbTp);
            }
            return;
        }

        FfmpegAudioEncoder.ExportViaWav(wavPath =>
        {
            RenderWav(wavPath);
            if (options.ComposeVideo && project.VideoLayers.Count > 0)
            {
                var muxed = Path.ChangeExtension(outputPath, ".mp4");
                var duration = ComputeVideoDurationSeconds(project, options, bpm);
                var startBeat = options.Kind == ExportKind.Region ? options.RegionStartBeat : 0;
                _videoCompositor.Export(project, wavPath, muxed, duration,
                    waveformCache: waveformCache, bpm: bpm, startBeat: startBeat, progress: progress);
                if (!muxed.Equals(outputPath, StringComparison.OrdinalIgnoreCase)
                    && outputPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    File.Move(muxed, outputPath, overwrite: true);
                return true;
            }

            if (options.MuxWithVideo && options.VideoTrackId is { } vid)
            {
                var layer = project.VideoLayers.FirstOrDefault(v => v.Id == vid);
                var videoPath = layer?.Items.FirstOrDefault(i => i.Kind == Models.Media.VideoElementKind.Video)?.SourcePath;
                if (layer is not null && !string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
                {
                    var muxed = Path.ChangeExtension(outputPath, ".mp4");
                    _videoMuxer.Mux(wavPath, videoPath, layer.OffsetSeconds, muxed,
                        layer.InPointSeconds, layer.OutPointSeconds > layer.InPointSeconds ? layer.OutPointSeconds : 0);
                    if (!muxed.Equals(outputPath, StringComparison.OrdinalIgnoreCase)
                        && outputPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                        File.Move(muxed, outputPath, overwrite: true);
                    return true;
                }
            }

            return false;
        }, outputPath, () => BuildEncoderMetadata(pendingReport, options.TargetIntegratedLufs));

        // Sidecar beside the *final* deliverable (encoded FLAC/MP3/OGG or muxed MP4), not the temp WAV.
        if (options.AnalyzeLoudness && pendingReport is { } finalReport)
            WriteLoudnessSidecars(outputPath, finalReport, options.WriteLoudnessJson, options.TargetIntegratedLufs);
    }

    private static double ComputeVideoDurationSeconds(Project project, ExportOptions options, double bpm)
    {
        if (options.Kind == ExportKind.Region && options.RegionEndBeat > options.RegionStartBeat)
            return (options.RegionEndBeat - options.RegionStartBeat) * 60.0 / Math.Max(1, bpm);
        var synced = project.VideoLayers.FirstOrDefault(l => l.HasVideoItem && l.OutPointSeconds > l.InPointSeconds);
        if (synced is not null)
            return synced.OutPointSeconds - synced.InPointSeconds;
        var beats = project.BarCount * Math.Max(1, project.TimeSignature.Numerator);
        return beats * 60.0 / Math.Max(1, bpm);
    }

    public Task ExportCompositedVideoAsync(Project project, string outputPath, double regionStartBeat,
        double regionEndBeat, IProgress<double>? progress = null,
        IVideoWaveformCacheService? waveformCache = null)
    {
        var format = new AudioFormat(48000, 2);
        var options = new ExportOptions
        {
            Kind = ExportKind.Region,
            RegionStartBeat = regionStartBeat,
            RegionEndBeat = regionEndBeat,
            ComposeVideo = true,
            AudioFormat = ExportAudioFormat.Wav
        };
        return Task.Run(() => Export(project, format, project.Tempo.BeatsPerMinute, outputPath, options, progress,
            waveformCache));
    }

    private static void ExportStems(Project project, AudioFormat format, double bpm, string folder,
        ExportOptions options, IProgress<double>? progress)
    {
        Directory.CreateDirectory(folder);
        ApplyDeliveryPlatformStatic(options);
        var ids = options.TrackIds?.ToHashSet();
        var targets = project.Tracks.Where(t => !t.IsBus && (ids is null || ids.Contains(t.Id))).ToList();

        if (options.MatchAlbumLoudness)
        {
            var tracks = new List<(string WavPath, float[] Samples, AudioFormat Format)>(targets.Count);
            for (var i = 0; i < targets.Count; i++)
            {
                var track = targets[i];
                var stemProject = CloneProjectForTrack(project, track, options.IncludeMasterFx);
                var sub = progress is null ? null : new Progress<double>(f =>
                    progress.Report((i + f) / Math.Max(1, targets.Count) * 0.75));
                var buffer = new OfflineRenderer().RenderMasterToBuffer(stemProject, format, bpm, sub,
                    surround: options.Surround);
                if (options.TargetSampleRate > 0 && options.TargetSampleRate != buffer.SampleRate)
                    buffer = ResampleBuffer(buffer, options.TargetSampleRate);
                tracks.Add((Path.Combine(folder, Sanitize(track.Name) + ".wav"), buffer.Samples,
                    new AudioFormat(buffer.SampleRate, buffer.Channels)));
            }

            MatchAlbumLoudness(tracks, options.TargetIntegratedLufs, options.TargetTruePeakDbTp);
            LoudnessReport? lastMatched = null;
            for (var i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                using (var writer = new WavWriter(track.WavPath, track.Format.Channels, track.Format.SampleRate,
                           options.BitDepth, options.ApplyDither, options.DitherMode))
                    writer.Write(track.Samples);
                lastMatched = LoudnessAnalyzer.Analyze(track.Samples, track.Format,
                    options.TargetIntegratedLufs, options.TargetTruePeakDbTp);
                WavLoudnessMetadata.Append(track.WavPath, lastMatched.Value.IntegratedLufs,
                    lastMatched.Value.TruePeakDbTp);
                if (options.AnalyzeLoudness)
                    WriteLoudnessSidecars(track.WavPath, lastMatched.Value, options.WriteLoudnessJson,
                        options.TargetIntegratedLufs);
                progress?.Report(0.75 + 0.25 * (i + 1) / Math.Max(1, tracks.Count));
            }
            options.LoudnessReport = lastMatched;
            return;
        }

        LoudnessReport? last = null;
        for (var i = 0; i < targets.Count; i++)
        {
            var track = targets[i];
            var stemProject = CloneProjectForTrack(project, track, options.IncludeMasterFx);
            var path = Path.Combine(folder, Sanitize(track.Name) + ".wav");
            var sub = progress is null ? null : new Progress<double>(f =>
                progress.Report((i + f) / targets.Count));

            if (options.AnalyzeLoudness || options.NormalizeLoudness)
            {
                last = RenderAnalyzedMaster(stemProject, format, bpm, path, options, sub);
                if (options.AnalyzeLoudness && last is { } lr)
                {
                    WriteLoudnessSidecars(path, lr, options.WriteLoudnessJson, options.TargetIntegratedLufs);
                    WavLoudnessMetadata.Append(path, lr.IntegratedLufs, lr.TruePeakDbTp);
                }
            }
            else
            {
                new OfflineRenderer().RenderToWav(stemProject, format, bpm, path, sub, options.BitDepth,
                    surround: options.Surround, applyDither: options.ApplyDither, skipAnalysers: true,
                    ditherMode: options.DitherMode);
            }
        }
        options.LoudnessReport = last;
    }

    private static void ExportBatch(Project project, AudioFormat format, double bpm, string folder,
        ExportOptions options, IProgress<double>? progress)
    {
        Directory.CreateDirectory(folder);
        ApplyDeliveryPlatformStatic(options);
        var path = Path.Combine(folder, Sanitize(project.Name) + ".wav");
        var restoreNormalize = options.NormalizeLoudness;
        if (options.MatchAlbumLoudness) options.NormalizeLoudness = true;
        if (options.AnalyzeLoudness || options.NormalizeLoudness)
        {
            options.LoudnessReport = RenderAnalyzedMaster(project, format, bpm, path, options, progress);
            if (options.AnalyzeLoudness && options.LoudnessReport is { } lr)
            {
                WriteLoudnessSidecars(path, lr, options.WriteLoudnessJson, options.TargetIntegratedLufs);
                WavLoudnessMetadata.Append(path, lr.IntegratedLufs, lr.TruePeakDbTp);
            }
        }
        else
        {
            new OfflineRenderer().RenderToWav(project, format, bpm, path, progress, options.BitDepth,
                surround: options.Surround, bypassMasterFx: options.BypassMasterFx,
                applyDither: options.ApplyDither, skipAnalysers: true, ditherMode: options.DitherMode);
        }
        options.NormalizeLoudness = restoreNormalize;
    }

    private static void ApplyDeliveryPlatformStatic(ExportOptions options)
    {
        if (DeliveryPlatformPresets.TryGet(options.DeliveryPlatform) is not { } p) return;
        options.TargetIntegratedLufs = p.Lufs;
        options.TargetTruePeakDbTp = p.DbTp;
    }

    /// <summary>Render master (or stem project) with optional normalize + loudness analysis to a WAV path.</summary>
    private static LoudnessReport? RenderAnalyzedMaster(Project project, AudioFormat format, double bpm,
        string wavPath, ExportOptions options, IProgress<double>? progress)
    {
        if (options.NormalizeLoudness)
        {
            var buf = new OfflineRenderer().RenderMasterToBuffer(project, format, bpm, progress,
                surround: options.Surround, bypassMasterFx: options.BypassMasterFx);
            var fmt = new AudioFormat(buf.SampleRate, buf.Channels);
            var report = LoudnessAnalyzer.Analyze(buf.Samples, fmt,
                options.TargetIntegratedLufs, options.TargetTruePeakDbTp);
            if (!float.IsNegativeInfinity(report.IntegratedLufs))
            {
                var gainDb = options.TargetIntegratedLufs - report.IntegratedLufs;
                var gain = (float)Math.Pow(10.0, gainDb / 20.0);
                for (var i = 0; i < buf.Samples.Length; i++)
                    buf.Samples[i] *= gain;
                if (gainDb > 0.05)
                    ApplyDeliveryLimiter(buf.Samples, fmt, options.TargetTruePeakDbTp);
                else
                    ApplyTruePeakCeiling(buf.Samples, fmt, options.TargetTruePeakDbTp);
            }
            else ApplyTruePeakCeiling(buf.Samples, fmt, options.TargetTruePeakDbTp);

            var loudness = new LoudnessAnalyzer();
            loudness.Prepare(fmt);
            using var writer = new WavWriter(wavPath, buf.Channels, buf.SampleRate, options.BitDepth,
                options.ApplyDither, options.DitherMode);
            const int block = 4096;
            for (var i = 0; i < buf.Samples.Length; i += block)
            {
                var len = Math.Min(block, buf.Samples.Length - i);
                var slice = buf.Samples.AsSpan(i, len);
                loudness.Process(slice);
                writer.Write(slice);
            }
            return loudness.Finish(options.TargetIntegratedLufs, options.TargetTruePeakDbTp);
        }

        var analyzer = new LoudnessAnalyzer();
        new OfflineRenderer().RenderToWav(project, format, bpm, wavPath, progress, options.BitDepth,
            surround: options.Surround, bypassMasterFx: options.BypassMasterFx,
            applyDither: options.ApplyDither, skipAnalysers: true, loudness: analyzer,
            ditherMode: options.DitherMode);
        return analyzer.Finish(options.TargetIntegratedLufs, options.TargetTruePeakDbTp);
    }

    /// <summary>Attenuates interleaved PCM so held true-peak ≤ <paramref name="ceilingDbTp"/>.</summary>
    public static void ApplyTruePeakCeiling(float[] samples, AudioFormat format, double ceilingDbTp)
    {
        var report = LoudnessAnalyzer.Analyze(samples, format, targetTruePeakDbTp: ceilingDbTp);
        if (report.TruePeakDbTp <= ceilingDbTp + 0.01) return;
        var backOffDb = ceilingDbTp - report.TruePeakDbTp;
        var gain = (float)Math.Pow(10.0, backOffDb / 20.0);
        for (var i = 0; i < samples.Length; i++)
            samples[i] *= gain;
    }

    /// <summary>
    /// Re-runs a 4×-oversampled Peak Limiter at the delivery ceiling after a post-chain loudness boost,
    /// so overs from the gain stage are limited rather than only attenuated.
    /// </summary>
    public static void ApplyDeliveryLimiter(float[] samples, AudioFormat format, double ceilingDbTp)
    {
        var lim = new PeakLimiterEffect
        {
            ThresholdDb = Math.Min(-0.5, ceilingDbTp - 0.5),
            CeilingDb = ceilingDbTp,
            ReleaseMs = 80,
            OversampleIndex = 2,
            SpectralLimiter = false
        };
        lim.Prepare(format);
        var ch = Math.Max(1, format.Channels);
        var blockFrames = 2048;
        var block = blockFrames * ch;
        for (var i = 0; i < samples.Length; i += block)
        {
            var len = Math.Min(block, samples.Length - i);
            // Align to whole frames.
            len -= len % ch;
            if (len <= 0) break;
            lim.Process(samples.AsSpan(i, len));
        }
        ApplyTruePeakCeiling(samples, format, ceilingDbTp);
    }

    /// <summary>Windowed-sinc resampling of interleaved PCM (48-tap Hann low-pass).</summary>
    public static AudioSampleBuffer ResampleBuffer(AudioSampleBuffer source, int targetRate)
    {
        if (targetRate <= 0 || targetRate == source.SampleRate)
            return source;
        var ch = Math.Max(1, source.Channels);
        var srcFrames = source.Samples.Length / ch;
        var ratio = (double)targetRate / source.SampleRate;
        var dstFrames = Math.Max(1, (int)Math.Round(srcFrames * ratio));
        var dst = new float[dstFrames * ch];
        const int taps = 48;
        const int half = taps / 2;
        var cutoff = Math.Min(1.0, ratio) * 0.95;
        for (var f = 0; f < dstFrames; f++)
        {
            var srcPos = f / ratio;
            var centre = (int)Math.Floor(srcPos);
            for (var c = 0; c < ch; c++)
            {
                double sum = 0;
                double weightSum = 0;
                for (var tap = -half + 1; tap <= half; tap++)
                {
                    var sourceFrame = Math.Clamp(centre + tap, 0, srcFrames - 1);
                    var x = (centre + tap - srcPos) * cutoff;
                    var sinc = Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
                    var windowPosition = (tap + half - 1.0) / (taps - 1.0);
                    var window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * windowPosition);
                    var weight = cutoff * sinc * window;
                    sum += source.Samples[sourceFrame * ch + c] * weight;
                    weightSum += weight;
                }
                dst[f * ch + c] = weightSum == 0 ? 0 : (float)(sum / weightSum);
            }
        }
        return new AudioSampleBuffer(dst, ch, targetRate);
    }

    /// <summary>
    /// Per-track gain (dB) to align an album to <paramref name="targetLufs"/> while preserving
    /// relative loudness — the loudest track hits the target and quieter tracks stay proportionally lower.
    /// </summary>
    public static double[] ComputeAlbumOffsets(IReadOnlyList<double> integratedLufs, double targetLufs)
    {
        if (integratedLufs.Count == 0) return Array.Empty<double>();
        var max = double.NegativeInfinity;
        foreach (var l in integratedLufs)
            if (!double.IsNegativeInfinity(l) && !double.IsNaN(l) && l > max) max = l;
        if (double.IsNegativeInfinity(max))
            return new double[integratedLufs.Count];
        var offset = targetLufs - max;
        var offsets = new double[integratedLufs.Count];
        for (var i = 0; i < offsets.Length; i++) offsets[i] = offset;
        return offsets;
    }

    /// <summary>Gain-normalises interleaved PCM toward <paramref name="targetLufs"/> with true-peak safety.
    /// When the first pass misses the target by more than 0.3 LU, a second corrective gain pass is applied.</summary>
    public static float[] NormalizeBufferToLufs(float[] samples, AudioFormat format, double targetLufs,
        double targetTp)
    {
        ApplyNormalizePass(samples, format, targetLufs, targetTp);
        var report = LoudnessAnalyzer.Analyze(samples, format, targetLufs, targetTp);
        if (!float.IsNegativeInfinity(report.IntegratedLufs) &&
            Math.Abs(targetLufs - report.IntegratedLufs) > 0.3)
            ApplyNormalizePass(samples, format, targetLufs, targetTp);
        return samples;
    }

    private static void ApplyNormalizePass(float[] samples, AudioFormat format, double targetLufs, double targetTp)
    {
        var report = LoudnessAnalyzer.Analyze(samples, format, targetLufs, targetTp);
        if (float.IsNegativeInfinity(report.IntegratedLufs)) return;
        var gainDb = targetLufs - report.IntegratedLufs;
        var gain = (float)Math.Pow(10.0, gainDb / 20.0);
        for (var i = 0; i < samples.Length; i++)
            samples[i] *= gain;
        if (gainDb > 0.05)
            ApplyDeliveryLimiter(samples, format, targetTp);
        else
            ApplyTruePeakCeiling(samples, format, targetTp);
    }

    /// <summary>
    /// Album loudness match: analyses each track, applies shared album offset toward
    /// <paramref name="targetLufs"/>, then true-peak limits each buffer in place.
    /// </summary>
    public static void MatchAlbumLoudness(
        IReadOnlyList<(string WavPath, float[] Samples, AudioFormat Format)> tracks, double targetLufs,
        double targetTp = -1.0)
    {
        if (tracks.Count == 0) return;
        var integrated = new double[tracks.Count];
        for (var i = 0; i < tracks.Count; i++)
        {
            var report = LoudnessAnalyzer.Analyze(tracks[i].Samples, tracks[i].Format, targetLufs, targetTp);
            integrated[i] = report.IntegratedLufs;
        }

        var offsets = ComputeAlbumOffsets(integrated, targetLufs);
        for (var i = 0; i < tracks.Count; i++)
        {
            var gain = (float)Math.Pow(10.0, offsets[i] / 20.0);
            var samples = tracks[i].Samples;
            for (var s = 0; s < samples.Length; s++)
                samples[s] *= gain;
            if (offsets[i] > 0.05)
                ApplyDeliveryLimiter(samples, tracks[i].Format, targetTp);
            else
                ApplyTruePeakCeiling(samples, tracks[i].Format, targetTp);
        }
    }

    public static void WriteLoudnessSidecars(string deliverablePath, LoudnessReport report, bool writeJson = true,
        double? targetLufs = null)
    {
        try
        {
            File.WriteAllText(deliverablePath + ".loudness.txt", report.Summary + Environment.NewLine);
            if (writeJson)
            {
                double? replayGain = null;
                if (targetLufs is { } tl && !float.IsNegativeInfinity(report.IntegratedLufs))
                    replayGain = tl - report.IntegratedLufs;
                var json = JsonSerializer.Serialize(new
                {
                    report.IntegratedLufs,
                    report.ShortTermMaxLufs,
                    report.MomentaryMaxLufs,
                    report.LoudnessRangeLu,
                    report.TruePeakDbTp,
                    report.SamplePeakDbFs,
                    report.WithinTarget,
                    ReplayGainTrackGainDb = replayGain,
                    report.Summary
                }, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
                });
                File.WriteAllText(deliverablePath + ".loudness.json", json);
            }
        }
        catch
        {
            // Sidecar is best-effort; the audio file is the deliverable.
        }
    }

    private static IReadOnlyDictionary<string, string>? BuildEncoderMetadata(LoudnessReport? report,
        double targetLufs)
    {
        if (report is not { } value || float.IsNegativeInfinity(value.IntegratedLufs)) return null;
        var gain = targetLufs - value.IntegratedLufs;
        var r128 = (int)Math.Round(gain * 256.0);
        return new Dictionary<string, string>
        {
            ["REPLAYGAIN_TRACK_GAIN"] = $"{gain:+0.00;-0.00;0.00} dB",
            ["R128_TRACK_GAIN"] = r128.ToString()
        };
    }

    private static void ExportComparisonPairFiles(Project project, AudioFormat format, double bpm,
        string masterWavPath, ExportOptions options, double? regionStart, double? regionEnd)
    {
        double? start = regionStart;
        double? end = regionEnd;
        if (start is null || end is null || end <= start)
        {
            start = 0;
            end = bpm > 1 ? 30.0 * bpm / 60.0 : 64;
        }

        var dir = Path.GetDirectoryName(masterWavPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(masterWavPath);
        var prePath = Path.Combine(dir, baseName + "-comparison-unmastered.wav");
        var masteredPath = Path.Combine(dir, baseName + "-comparison-mastered.wav");

        new OfflineRenderer().RenderToWav(project, format, bpm, prePath, null, options.BitDepth,
            start, end, options.Surround, bypassMasterFx: true, applyDither: options.ApplyDither,
            skipAnalysers: true, ditherMode: options.DitherMode);
        new OfflineRenderer().RenderToWav(project, format, bpm, masteredPath, null, options.BitDepth,
            start, end, options.Surround, bypassMasterFx: false, applyDither: options.ApplyDither,
            skipAnalysers: true, ditherMode: options.DitherMode);
    }

    private static Project CloneProjectForTrack(Project source, Track track, bool includeMasterFx = true) =>
        CloneProjectForTrackExport(source, track, includeMasterFx);

    /// <summary>
    /// Builds a single-track project for stem render. When <paramref name="includeMasterFx"/> is true,
    /// the Master track (and its insert chain) is cloned so stems bake through master processing.
    /// </summary>
    public static Project CloneProjectForTrackExport(Project source, Track track, bool includeMasterFx = true)
    {
        var p = new Project { Name = track.Name, Tempo = source.Tempo, TimeSignature = source.TimeSignature, BarCount = source.BarCount };
        var clone = CloneTrackShallow(track);
        p.Tracks.Add(clone);

        var needed = new HashSet<Guid> { track.Id };
        foreach (var send in track.Sends)
            if (send.Enabled) needed.Add(send.TargetTrackId);
        AddAncestors(track, source, needed);

        foreach (var t in source.Tracks)
        {
            if (t.Id == track.Id) continue;
            if (t.IsBus && needed.Contains(t.Id))
                p.Tracks.Add(CloneTrackShallow(t));
        }

        if (includeMasterFx && source.Master is { } m && !p.Tracks.Any(t => t.Kind == TrackKind.Master))
            p.Tracks.Add(CloneTrackShallow(m));
        return p;
    }

    private static void AddAncestors(Track track, Project source, HashSet<Guid> needed)
    {
        var pid = track.ParentId;
        var guard = 0;
        while (pid is { } id && guard++ < 64)
        {
            needed.Add(id);
            pid = source.Tracks.FirstOrDefault(t => t.Id == id)?.ParentId;
        }
    }

    private static Track CloneTrackShallow(Track track)
    {
        var clone = new Track
        {
            Id = track.Id,
            Name = track.Name,
            Kind = track.Kind,
            ParentId = track.ParentId,
            Volume = track.Volume,
            Pan = track.Pan,
            OutputTarget = track.OutputTarget,
            OutputBusId = track.OutputBusId,
            RouteToMaster = track.RouteToMaster
        };
        foreach (var c in track.Clips) clone.Clips.Add(c);
        foreach (var slot in track.Instruments) clone.Instruments.Add(slot);
        foreach (var fx in track.Effects) clone.Effects.Add(fx);
        foreach (var send in track.Sends) clone.Sends.Add(new TrackSend
        {
            Id = send.Id,
            TargetTrackId = send.TargetTrackId,
            Level = send.Level,
            PreFader = send.PreFader,
            Enabled = send.Enabled
        });
        clone.CommitInstruments();
        clone.CommitEffects();
        clone.CommitMidiEffects();
        return clone;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "export" : name;
    }
}
