using System;
using System.Linq;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>Integrates tempo automation to convert beats ↔ seconds.</summary>
public sealed class TempoMapService : ITempoMapService
{
    private const int IntegrationStepsPerBeat = 32;

    public double BeatsToSeconds(Project project, double beat)
    {
        if (beat <= 0) return 0;
        var fallback = project.Tempo.BeatsPerMinute;
        var lane = FindTempoLane(project);
        if (lane is null || lane.Points.Count == 0)
            return beat * 60.0 / Math.Max(fallback, 1);

        var seconds = 0.0;
        var cursor = 0.0;
        while (cursor < beat - 1e-9)
        {
            var end = Math.Min(beat, NextBreakpoint(lane, cursor));
            var mid = (cursor + end) * 0.5;
            var bpm = Math.Clamp(lane.Evaluate(mid),
                ProjectAutomationTargets.MinBpm, ProjectAutomationTargets.MaxBpm);
            seconds += (end - cursor) * 60.0 / Math.Max(bpm, 1);
            cursor = end;
        }

        return seconds;
    }

    public double SecondsToBeats(Project project, double seconds)
    {
        if (seconds <= 0) return 0;
        var fallback = project.Tempo.BeatsPerMinute;
        var lane = FindTempoLane(project);
        if (lane is null || lane.Points.Count == 0)
            return seconds * Math.Max(fallback, 1) / 60.0;

        var elapsed = 0.0;
        var beat = 0.0;
        var maxBeat = lane.Points[^1].Beat + 4096;
        while (elapsed < seconds - 1e-9 && beat < maxBeat)
        {
            var endBeat = Math.Min(NextBreakpoint(lane, beat), beat + 1.0 / IntegrationStepsPerBeat);
            var mid = (beat + endBeat) * 0.5;
            var bpm = Math.Clamp(lane.Evaluate(mid),
                ProjectAutomationTargets.MinBpm, ProjectAutomationTargets.MaxBpm);
            var segSec = (endBeat - beat) * 60.0 / Math.Max(bpm, 1);
            if (elapsed + segSec >= seconds)
            {
                var frac = (seconds - elapsed) / Math.Max(segSec, 1e-12);
                return beat + (endBeat - beat) * frac;
            }

            elapsed += segSec;
            beat = endBeat;
        }

        return beat;
    }

    public double TempoAtBeat(Project project, double beat)
        => OfflineAutomationDriver.ResolveTempo(project, beat, project.Tempo.BeatsPerMinute);

    private static AutomationLane? FindTempoLane(Project project)
    {
        var master = project.Master;
        if (master is null) return null;
        return master.ActiveAutoLanes.FirstOrDefault(l => l.Binding?.Kind == AutomationTargetKind.Tempo);
    }

    private static double NextBreakpoint(AutomationLane lane, double beat)
    {
        foreach (var p in lane.Points)
        {
            if (p.Beat > beat + 1e-9)
                return p.Beat;
        }

        return beat + 1.0;
    }
}
