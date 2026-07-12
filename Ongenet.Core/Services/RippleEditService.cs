using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Services;

/// <summary>Timeline ripple editing — shifting clips when inserting or deleting time.</summary>
public static class RippleEditService
{
    public static void InsertTime(Project project, double atBeat, double amountBeats)
    {
        if (amountBeats <= 0) return;
        ShiftClips(project, beat => beat >= atBeat - 1e-9, amountBeats);
    }

    public static void DeleteTime(Project project, double atBeat, double amountBeats)
    {
        if (amountBeats <= 0) return;
        ShiftClips(project, beat => beat >= atBeat + amountBeats - 1e-9, -amountBeats);
        RemoveClipsInRange(project, atBeat, atBeat + amountBeats);
    }

    private static void ShiftClips(Project project, Func<double, bool> beatPredicate, double delta)
    {
        foreach (var track in project.Tracks)
        foreach (var clip in track.Clips.Where(c => beatPredicate(c.StartBeat)))
            clip.StartBeat += delta;

        foreach (var pc in project.PatternClips.Where(pc => beatPredicate(pc.StartBeat)))
            pc.StartBeat += delta;
    }

    private static void RemoveClipsInRange(Project project, double start, double end)
    {
        foreach (var track in project.Tracks)
            track.Clips.RemoveAll(c => c.StartBeat >= start - 1e-9 && c.EndBeat <= end + 1e-9);
    }
}
