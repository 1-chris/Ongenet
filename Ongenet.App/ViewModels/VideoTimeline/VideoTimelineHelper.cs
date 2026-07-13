using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.App.ViewModels.Panels;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.ViewModels.VideoTimeline;

public static class VideoTimelineHelper
{
    public static string LayerLabel(VideoLayer layer) => layer.Name;

    public static string LayerWaveformLabel(VideoLayer layer, Project project, string noneLabel, string unknownTrack)
    {
        if (!layer.IsWaveformLayer) return string.Empty;
        if (layer.AudioSourceTrackId is not { } id) return noneLabel;
        var track = project.Tracks.FirstOrDefault(t => t.Id == id);
        return track?.Name ?? unknownTrack;
    }

    public static string LayerItemLabel(VideoLayerItem item, string emptyLabel) =>
        string.IsNullOrWhiteSpace(item.SourcePath) ? emptyLabel : System.IO.Path.GetFileName(item.SourcePath);

    public static string TriggerLabel(VideoTrigger tr, Project project,
        IReadOnlyList<ClipSyncOption> clips, IReadOnlyList<VideoLayer> layers,
        string anyClip, string unknownLayer,
        IReadOnlyList<EnumOption<VideoTriggerMoment>> moments,
        IReadOnlyList<EnumOption<VideoTriggerAction>> actions)
    {
        var clip = clips.FirstOrDefault(c => c.ClipId == tr.ClipId);
        var target = layers.FirstOrDefault(l => l.Id == tr.TargetLayerId);
        var moment = moments.FirstOrDefault(o => o.Value == tr.Moment)?.Label ?? tr.Moment.ToString();
        var action = actions.FirstOrDefault(o => o.Value == tr.Action)?.Label ?? tr.Action.ToString();
        var clipName = clip?.ClipName ?? anyClip;
        var targetName = target?.Name ?? unknownLayer;
        return $"{clipName} · {moment} · {action} → {targetName}";
    }

    public static Clip? FindClip(Project project, Guid? clipId)
    {
        if (clipId is null) return null;
        foreach (var track in project.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip is not null) return clip;
        }
        return null;
    }

    public static double TriggerBeat(VideoTrigger tr, Project project)
    {
        var clip = FindClip(project, tr.ClipId);
        if (clip is null) return 0;
        return tr.Moment == VideoTriggerMoment.ClipEnd ? clip.EndBeat : clip.StartBeat;
    }

    public static (double StartBeat, double EndBeat) LayerSpan(
        VideoLayer layer, Project project, ITempoMapService tempoMap, double defaultEndBeat)
    {
        if (layer.SyncClipId is { } syncId)
        {
            var clip = FindClip(project, syncId);
            if (clip is not null)
            {
                var clipDur = tempoMap.BeatsToSeconds(project, clip.EndBeat)
                              - tempoMap.BeatsToSeconds(project, clip.StartBeat);
                var trimDur = layer.OutPointSeconds > layer.InPointSeconds
                    ? layer.OutPointSeconds - layer.InPointSeconds
                    : clipDur;
                var widthBeats = clipDur > 0
                    ? (clip.EndBeat - clip.StartBeat) * (trimDur / clipDur)
                    : clip.EndBeat - clip.StartBeat;
                return (clip.StartBeat, clip.StartBeat + Math.Max(0.25, widthBeats));
            }
        }

        return (0, defaultEndBeat);
    }
}
