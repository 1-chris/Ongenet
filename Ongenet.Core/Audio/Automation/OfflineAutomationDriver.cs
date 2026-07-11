using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Automation;

/// <summary>
/// Drives automation curves into live <see cref="Track"/> models during offline render, then syncs
/// cloned effect/instrument parameters so the render clones mirror the automated state. Shared by
/// <see cref="OfflineRenderer"/> and refactored from the live <see cref="AudioEngine"/> automation path.
/// </summary>
public static class OfflineAutomationDriver
{
    /// <summary>Drives each automation lane's target from its curve at <paramref name="beat"/>.</summary>
    public static void ApplyTrack(Track track, double beat, bool skipArmedLanes = true)
    {
        var lanes = track.ActiveAutoLanes;
        if (lanes.Length == 0) return;
        foreach (var lane in lanes)
        {
            if (skipArmedLanes && lane.IsArmed) continue;
            lane.Target.Write(lane.Evaluate(beat));
        }
    }

    /// <summary>
    /// The tempo (BPM) in force at <paramref name="beat"/>: the master track's Tempo automation curve
    /// when one exists, otherwise <paramref name="fallbackBpm"/>.
    /// </summary>
    public static double ResolveTempo(Project project, double beat, double fallbackBpm)
    {
        var master = project.Master;
        if (master is not null)
        {
            foreach (var lane in master.ActiveAutoLanes)
            {
                if (lane.Binding?.Kind != AutomationTargetKind.Tempo) continue;
                if (lane.IsArmed) break;
                return Math.Clamp(lane.Evaluate(beat),
                    ProjectAutomationTargets.MinBpm, ProjectAutomationTargets.MaxBpm);
            }
        }

        return fallbackBpm > 0 ? fallbackBpm : 120.0;
    }

    /// <summary>
    /// Collects track ids referenced as sidechain/carrier sources by any <see cref="ISourceTrackEffect"/>.
    /// </summary>
    public static HashSet<Guid> CollectSidechainSources(IEnumerable<Track> tracks)
    {
        var set = new HashSet<Guid>();
        foreach (var track in tracks)
        {
            CollectSidechainSourcesFromEffects(track.ActiveEffects, set);
            foreach (var slot in track.ActiveInstruments)
                CollectSidechainSourcesFromEffects(slot.ActiveEffects, set);
        }

        return set;
    }

    private static void CollectSidechainSourcesFromEffects(IAudioEffect[] effects, HashSet<Guid> set)
    {
        foreach (var fx in effects)
        {
            if (fx is ISourceTrackEffect { SourceTrackId: { } id } && id != Guid.Empty)
                set.Add(id);
        }
    }

    /// <summary>Copies enabled state and parameter values from live effects to their clones.</summary>
    public static void SyncEffects(IAudioEffect[] live, IAudioEffect[] clone)
    {
        var n = Math.Min(live.Length, clone.Length);
        for (var i = 0; i < n; i++) SyncEffect(live[i], clone[i]);
    }

    /// <summary>Copies parameter values from a live instrument to its clone.</summary>
    public static void SyncInstrument(IInstrument live, IInstrument clone)
        => SyncParameters(live.Parameters, clone.Parameters);

    private static void SyncEffect(IAudioEffect live, IAudioEffect clone)
    {
        clone.Enabled = live.Enabled;
        SyncParameters(live.Parameters, clone.Parameters);
    }

    private static void SyncParameters(IReadOnlyList<Parameter> live, IReadOnlyList<Parameter> clone)
    {
        var n = Math.Min(live.Count, clone.Count);
        for (var i = 0; i < n; i++)
        {
            switch (live[i])
            {
                case FloatParameter lf when clone[i] is FloatParameter cf:
                    cf.Value = lf.Value;
                    break;
                case BoolParameter lb when clone[i] is BoolParameter cb:
                    cb.Value = lb.Value;
                    break;
                case ChoiceParameter lc when clone[i] is ChoiceParameter cc:
                    cc.SelectedIndex = lc.SelectedIndex;
                    break;
            }
        }
    }
}
