using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
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
    public SurroundFormat Surround { get; set; } = SurroundFormat.Stereo;
    public ExportAudioFormat AudioFormat { get; set; } = ExportAudioFormat.Wav;
    public bool MuxWithVideo { get; set; }
    public Guid? VideoTrackId { get; set; }
    public bool ComposeVideo { get; set; }
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
    }

    public void Export(Project project, AudioFormat format, double bpm, string outputPath,
        ExportOptions options, IProgress<double>? progress = null,
        IVideoWaveformCacheService? waveformCache = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var regionStart = options.Kind == ExportKind.Region ? (double?)options.RegionStartBeat : null;
        var regionEnd = options.Kind == ExportKind.Region ? (double?)options.RegionEndBeat : null;

        void RenderWav(string wavPath)
        {
            switch (options.Kind)
            {
                case ExportKind.Master:
                case ExportKind.Region:
                    new OfflineRenderer().RenderToWav(project, format, bpm, wavPath, progress,
                        options.BitDepth, regionStart, regionEnd, options.Surround);
                    break;
                case ExportKind.Stems:
                    ExportStems(project, format, bpm, Path.GetDirectoryName(wavPath)!, options, progress);
                    return;
                case ExportKind.Batch:
                    ExportBatch(project, format, bpm, Path.GetDirectoryName(wavPath)!, options, progress);
                    return;
            }
        }

        if (options.AudioFormat == ExportAudioFormat.Wav && !options.MuxWithVideo
            && !(options.ComposeVideo && project.VideoLayers.Count > 0))
        {
            RenderWav(outputPath);
            return;
        }

        FfmpegAudioEncoder.ExportViaWav(wavPath =>
        {
            RenderWav(wavPath);
            if (options.ComposeVideo && project.VideoLayers.Count > 0)
            {
                var muxed = Path.ChangeExtension(outputPath, ".mp4");
                var duration = ComputeVideoDurationSeconds(project, options, bpm);
                FfmpegVideoCompositor.Export(project, wavPath, muxed, duration,
                    waveformCache: waveformCache, bpm: bpm);
                if (!muxed.Equals(outputPath, StringComparison.OrdinalIgnoreCase)
                    && outputPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    File.Move(muxed, outputPath, overwrite: true);
                return;
            }

            if (options.MuxWithVideo && options.VideoTrackId is { } vid)
            {
                var layer = project.VideoLayers.FirstOrDefault(v => v.Id == vid);
                var videoPath = layer?.Items.FirstOrDefault(i => i.Kind == Models.Media.VideoElementKind.Video)?.SourcePath;
                if (layer is not null && !string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
                {
                    var muxed = Path.ChangeExtension(outputPath, ".mp4");
                    FfmpegVideoMuxer.Mux(wavPath, videoPath, layer.OffsetSeconds, muxed,
                        layer.InPointSeconds, layer.OutPointSeconds > layer.InPointSeconds ? layer.OutPointSeconds : 0);
                    if (!muxed.Equals(outputPath, StringComparison.OrdinalIgnoreCase)
                        && outputPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                        File.Move(muxed, outputPath, overwrite: true);
                    return;
                }
            }
        }, outputPath);
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

    private static void ExportStems(Project project, AudioFormat format, double bpm, string folder,
        ExportOptions options, IProgress<double>? progress)
    {
        Directory.CreateDirectory(folder);
        var ids = options.TrackIds?.ToHashSet();
        var targets = project.Tracks.Where(t => !t.IsBus && (ids is null || ids.Contains(t.Id))).ToList();
        for (var i = 0; i < targets.Count; i++)
        {
            var track = targets[i];
            var stemProject = CloneProjectForTrack(project, track);
            var path = Path.Combine(folder, Sanitize(track.Name) + ".wav");
            var sub = progress is null ? null : new Progress<double>(f =>
                progress.Report((i + f) / targets.Count));
            new OfflineRenderer().RenderToWav(stemProject, format, bpm, path, sub, options.BitDepth,
                surround: options.Surround);
        }
    }

    private static void ExportBatch(Project project, AudioFormat format, double bpm, string folder,
        ExportOptions options, IProgress<double>? progress)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Sanitize(project.Name) + ".wav");
        new OfflineRenderer().RenderToWav(project, format, bpm, path, progress, options.BitDepth,
            surround: options.Surround);
    }

    private static Project CloneProjectForTrack(Project source, Track track) =>
        CloneProjectForTrackExport(source, track);

    internal static Project CloneProjectForTrackExport(Project source, Track track)
    {
        var p = new Project { Name = track.Name, Tempo = source.Tempo, TimeSignature = source.TimeSignature, BarCount = source.BarCount };
        var clone = CloneTrackShallow(track);
        p.Tracks.Add(clone);

        // Include return buses this track sends to, and ancestor groups.
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

        if (source.Master is { } m && !p.Tracks.Any(t => t.Kind == TrackKind.Master))
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
