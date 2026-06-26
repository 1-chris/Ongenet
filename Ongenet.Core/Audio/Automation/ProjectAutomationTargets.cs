using System;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Automation;

/// <summary>
/// Builders for the project-level automation targets (Tempo, Time signature). These don't belong to any
/// single track's instrument/effect chain — they drive the global <see cref="Project"/> values — but are
/// hosted as lanes on the master track so they edit/record/play back through the same pipeline. Kept in
/// one place so the UI gesture and the save/load rebind produce identical targets and ranges.
/// </summary>
public static class ProjectAutomationTargets
{
    /// <summary>Tempo automation range, in BPM (matches the transport's tempo editor).</summary>
    public const double MinBpm = 1.0;
    public const double MaxBpm = 999.0;

    /// <summary>Time-signature numerator (beats per bar) automation range.</summary>
    public const double MinBeatsPerBar = 1.0;
    public const double MaxBeatsPerBar = 32.0;

    /// <summary>A lane target driving <see cref="Project.Tempo"/> (BPM), clamped to the editor's range.</summary>
    public static IAutomationTarget Tempo(Project project)
        => new DelegateAutomationTarget("Tempo", MinBpm, MaxBpm,
            () => project.Tempo.BeatsPerMinute,
            v => project.Tempo = new Tempo(Math.Clamp(v, MinBpm, MaxBpm)))
        {
            BindKind = AutomationTargetKind.Tempo,
            BindSource = project
        };

    /// <summary>
    /// A stepped lane target driving the time-signature numerator (beats per bar). The denominator is
    /// left untouched (it's a manual, mostly-cosmetic choice).
    /// </summary>
    public static IAutomationTarget TimeSignature(Project project)
        => new DelegateAutomationTarget("Time Signature", MinBeatsPerBar, MaxBeatsPerBar,
            () => project.TimeSignature.Numerator,
            v =>
            {
                var num = (int)Math.Round(Math.Clamp(v, MinBeatsPerBar, MaxBeatsPerBar));
                project.TimeSignature = new TimeSignature(num, project.TimeSignature.Denominator);
            }, stepped: true)
        {
            BindKind = AutomationTargetKind.TimeSignature,
            BindSource = project
        };
}
