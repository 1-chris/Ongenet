using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;

namespace Ongenet.VideoComposition.Rendering;

/// <summary>Resolves subtitle text from SRT files or arrangement clip lyrics at a given time.</summary>
public static class VideoSubtitleResolver
{
    private static readonly Dictionary<string, List<SrtCue>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? ResolveText(VideoLayerItem item, Project project, double timeSeconds,
        Func<Project, double, double>? beatsToSeconds = null, double playheadBeats = 0)
    {
        if (item.Kind != VideoElementKind.Subtitle) return null;

        if (!string.IsNullOrWhiteSpace(item.SubtitleSrtPath) && File.Exists(item.SubtitleSrtPath))
            return ResolveSrt(item.SubtitleSrtPath, timeSeconds);

        if (item.SubtitleClipId is { } clipId)
            return ResolveClipSubtitle(project, clipId, playheadBeats);

        return item.TextContent;
    }

    private static string? ResolveClipSubtitle(Project project, Guid clipId, double playheadBeats)
    {
        var clip = project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null || playheadBeats < clip.StartBeat || playheadBeats >= clip.EndBeat) return null;
        return string.IsNullOrWhiteSpace(clip.Name) ? null : clip.Name;
    }

    private static string? ResolveSrt(string path, double timeSeconds)
    {
        if (!Cache.TryGetValue(path, out var cues))
        {
            cues = ParseSrt(File.ReadAllText(path));
            Cache[path] = cues;
        }

        foreach (var cue in cues)
        {
            if (timeSeconds >= cue.Start && timeSeconds < cue.End)
                return cue.Text;
        }

        return null;
    }

    private static List<SrtCue> ParseSrt(string content)
    {
        var cues = new List<SrtCue>();
        var blocks = content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) continue;
            var timeLine = lines.FirstOrDefault(l => l.Contains("-->"));
            if (timeLine is null) continue;
            var parts = timeLine.Split("-->", StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            if (!TryParseSrtTime(parts[0], out var start) || !TryParseSrtTime(parts[1], out var end)) continue;
            var text = string.Join('\n', lines.SkipWhile(l => l != timeLine).Skip(1));
            cues.Add(new SrtCue(start, end, text));
        }

        return cues;
    }

    private static bool TryParseSrtTime(string raw, out double seconds)
    {
        seconds = 0;
        raw = raw.Trim().Replace(',', '.');
        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var ts))
        {
            seconds = ts.TotalSeconds;
            return true;
        }

        return false;
    }

    private sealed record SrtCue(double Start, double End, string Text);
}
