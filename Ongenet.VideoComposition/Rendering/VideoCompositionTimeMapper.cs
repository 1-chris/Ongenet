using System;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;

namespace Ongenet.VideoComposition.Rendering;

/// <summary>Maps transport time to per-layer local media time.</summary>
public static class VideoCompositionTimeMapper
{
    public static double ComputeLayerTimeSeconds(VideoLayer layer, double transportSeconds, Project project,
        Func<Project, double, double>? beatsToSeconds = null, double playheadBeats = 0)
    {
        var seconds = transportSeconds;
        if (layer.SyncClipId is { } clipId)
        {
            var clip = project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            if (clip is not null && beatsToSeconds is not null)
                seconds = beatsToSeconds(project, Math.Max(0, playheadBeats - clip.StartBeat));
        }

        var raw = layer.OffsetSeconds + seconds;
        var inPt = layer.InPointSeconds;
        var outPt = layer.OutPointSeconds;
        if (outPt > inPt && raw > outPt) return outPt;
        return Math.Max(inPt, raw);
    }

    public static bool IsLayerActiveAtTime(VideoLayer layer, double layerLocalTime)
    {
        var inPt = layer.InPointSeconds;
        var outPt = layer.OutPointSeconds;
        if (outPt > inPt)
            return layerLocalTime >= inPt && layerLocalTime <= outPt;
        return layerLocalTime >= inPt;
    }

    public static double ResolveExportFps(Project project)
    {
        if (project.VideoExportFps > 0)
            return project.VideoExportFps;
        var layerFps = project.VideoLayers
            .Where(l => l.Fps > 0)
            .Select(l => l.Fps)
            .DefaultIfEmpty(0)
            .Max();
        return layerFps > 0 ? layerFps : 25;
    }
}
