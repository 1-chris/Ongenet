using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.VideoComposition.Services;

/// <summary>Offline-renders track/bus audio into waveform peaks for video layer preview and export.</summary>
public sealed class VideoWaveformCacheService : IVideoWaveformCacheService
{
    private readonly OfflineRenderer _renderer = new();
    private readonly Dictionary<Guid, AudioWaveform> _cache = new();
    private readonly Dictionary<Guid, string> _stemWavPaths = new();
    private int _revision;

    public int Revision => _revision;

    public void Invalidate()
    {
        foreach (var path in _stemWavPaths.Values)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }

        _stemWavPaths.Clear();
        _cache.Clear();
        _revision++;
    }

    public AudioWaveform? TryGet(Guid trackId) =>
        _cache.TryGetValue(trackId, out var wf) ? wf : null;

    public AudioWaveform GetOrBuild(Project project, Guid trackId, double bpm, IProgress<double>? progress = null)
    {
        if (_cache.TryGetValue(trackId, out var cached))
            return cached;

        var buffer = RenderAudioBuffer(project, trackId, bpm, progress);
        var waveform = AudioWaveform.Build(buffer);
        _cache[trackId] = waveform;
        return waveform;
    }

    public AudioSampleBuffer GetOrBuildStemBuffer(Project project, Guid trackId, double bpm,
        IProgress<double>? progress = null) => RenderAudioBuffer(project, trackId, bpm, progress);

    public string GetOrBuildStemWavPath(Project project, Guid trackId, double bpm, IProgress<double>? progress = null)
    {
        if (_stemWavPaths.TryGetValue(trackId, out var cached) && File.Exists(cached))
            return cached;

        var buffer = RenderAudioBuffer(project, trackId, bpm, progress);
        var path = Path.Combine(Path.GetTempPath(), $"ongenet-stem-{trackId:N}.wav");
        using (var writer = new WavWriter(path, buffer.Channels, buffer.SampleRate, 16))
            writer.Write(buffer.Samples);

        _stemWavPaths[trackId] = path;
        return path;
    }

    private AudioSampleBuffer RenderAudioBuffer(Project project, Guid trackId, double bpm, IProgress<double>? progress)
    {
        var track = project.Tracks.FirstOrDefault(t => t.Id == trackId)
            ?? throw new InvalidOperationException("Audio source track not found.");

        var beatsPerBar = Math.Max(1, project.TimeSignature.Numerator);
        var totalBeats = Math.Max(1, project.BarCount * beatsPerBar);
        var scope = BuildScope(project, track, totalBeats);
        var format = new AudioFormat { SampleRate = 48000, Channels = 2 };
        return scope is not null
            ? _renderer.RenderScopeToBuffer(project, format, bpm, scope, progress)
            : RenderStem(project, track, format, bpm, totalBeats, progress);
    }

    private static ClipRenderScope? BuildScope(Project project, Track track, double totalBeats)
    {
        return track.Kind switch
        {
            TrackKind.Group => BuildGroupScope(project, track, totalBeats),
            TrackKind.Return => BuildReturnScope(project, track, totalBeats),
            TrackKind.Master => null,
            _ => BuildTrackScope(project, track, totalBeats)
        };
    }

    private static ClipRenderScope BuildGroupScope(Project project, Track group, double totalBeats)
    {
        var descendants = CollectDescendantTracks(project, group.Id).ToList();
        return ClipRenderScope.ForGroup(project, group, 0, totalBeats, descendants);
    }

    private static ClipRenderScope BuildReturnScope(Project project, Track returnTrack, double totalBeats)
    {
        var scopeEnd = totalBeats;
        var content = new Dictionary<Track, IReadOnlyList<Clip>>();
        foreach (var track in project.Tracks.Where(t => !t.IsBus))
        {
            if (!track.Sends.Any(s => s.Enabled && s.TargetTrackId == returnTrack.Id)) continue;
            var clips = track.Clips.Where(c => c.StartBeat < scopeEnd && c.EndBeat > 0).ToList();
            if (clips.Count > 0) content[track] = clips;
        }

        return new ClipRenderScope
        {
            StartBeat = 0,
            LengthBeats = totalBeats,
            ContentByTrack = content,
            TapAfterGroupId = returnTrack.Id,
            SidechainSourceIds = CollectSidechainIds(project, content.Keys, returnTrack.Id)
        };
    }

    private static ClipRenderScope BuildTrackScope(Project project, Track track, double totalBeats)
    {
        var scopeEnd = totalBeats;
        var clips = track.Clips.Where(c => c.StartBeat < scopeEnd && c.EndBeat > 0).ToList();
        var content = clips.Count > 0
            ? new Dictionary<Track, IReadOnlyList<Clip>> { [track] = clips }
            : new Dictionary<Track, IReadOnlyList<Clip>>();

        var tapGroupId = TopmostGroupAncestor(project, track.ParentId);
        return new ClipRenderScope
        {
            StartBeat = 0,
            LengthBeats = totalBeats,
            ContentByTrack = content,
            TapAfterGroupId = tapGroupId,
            SidechainSourceIds = CollectSidechainIds(project, content.Keys, tapGroupId)
        };
    }

    private AudioSampleBuffer RenderStem(Project project, Track track, AudioFormat format, double bpm,
        double totalBeats, IProgress<double>? progress)
    {
        var stemProject = ExportService.CloneProjectForTrackExport(project, track);
        return _renderer.RenderMasterToBuffer(stemProject, format, bpm, progress, 0, totalBeats);
    }

    private static IEnumerable<Track> CollectDescendantTracks(Project project, Guid groupId)
    {
        foreach (var track in project.Tracks)
        {
            if (IsDescendantOf(project, track, groupId))
                yield return track;
        }
    }

    private static bool IsDescendantOf(Project project, Track track, Guid ancestorId)
    {
        var pid = track.ParentId;
        var guard = 0;
        while (pid is { } id && guard++ < 64)
        {
            if (id == ancestorId) return true;
            pid = project.Tracks.FirstOrDefault(t => t.Id == id)?.ParentId;
        }

        return false;
    }

    private static Guid? TopmostGroupAncestor(Project project, Guid? parentId)
    {
        if (parentId is not { } id) return null;
        var trackById = project.Tracks.ToDictionary(t => t.Id);
        Guid? topmost = null;
        var cur = trackById.GetValueOrDefault(id);
        var guard = 0;
        while (cur is { Kind: TrackKind.Group } && guard++ < 64)
        {
            topmost = cur.Id;
            cur = cur.ParentId is { } pid ? trackById.GetValueOrDefault(pid) : null;
        }

        return topmost;
    }

    private static HashSet<Guid> CollectSidechainIds(Project project, IEnumerable<Track> contentTracks, Guid? tapGroupId)
    {
        var relevant = new HashSet<Guid>();
        foreach (var t in contentTracks) relevant.Add(t.Id);
        if (tapGroupId is { } gid) relevant.Add(gid);

        var needed = new HashSet<Guid>();
        foreach (var track in project.Tracks)
        {
            if (!relevant.Contains(track.Id)) continue;
            CollectFromEffects(track.Effects, relevant, needed);
            foreach (var slot in track.Instruments)
                CollectFromEffects(slot.Effects, relevant, needed);
        }

        return needed;
    }

    private static void CollectFromEffects(List<IAudioEffect> effects,
        HashSet<Guid> relevant, HashSet<Guid> needed)
    {
        foreach (var fx in effects)
        {
            if (fx is ISourceTrackEffect { SourceTrackId: { } id } && id != Guid.Empty && !relevant.Contains(id))
                needed.Add(id);
        }
    }
}
