using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>
/// Evaluates track modulators at schedule time and writes into their bound targets.
/// Supports legacy <see cref="TrackModulator"/> entries and registry-backed <see cref="ModulatorSlot"/>s.
/// </summary>
public static class TrackModulatorDriver
{
    private static readonly Dictionary<Guid, float> _envLevel = new();

    public static void ApplyTrack(Track track, double beat, double bpm, Project? project = null)
    {
        var timeSec = beat * 60.0 / (bpm > 0 ? bpm : 120.0);

        foreach (var mod in track.ActiveModulators)
        {
            if (!mod.Enabled) continue;
            ApplyLegacyModulator(track, mod, timeSec, beat, bpm, project);
        }

        foreach (var slot in track.ActiveModulatorSlots)
        {
            if (!slot.Enabled || !slot.Source.Enabled) continue;
            ApplyRegistrySlot(track, slot, timeSec, beat, bpm, project);
        }
    }

    private static void ApplyRegistrySlot(Track track, ModulatorSlot slot, double timeSec, double beat,
        double bpm, Project? project)
    {
        var target = ProjectFile.BuildTarget(track, (int)slot.Target.Kind,
            slot.Target.EffectIndex, slot.Target.ParamIndex, project);
        if (target is null) return;

        var ctx = new ModulatorContext
        {
            Track = track,
            TimeSec = timeSec,
            Beat = beat,
            Bpm = bpm,
            Project = project,
            SlotId = slot.Id
        };

        var uni = Math.Clamp(slot.Source.Evaluate(ctx), 0, 1);
        var depth = Math.Clamp(slot.Depth, 0, 1);
        var current = target.Read();
        var modulated = Blend(current, uni, depth, slot.Target.Kind, target.Minimum, target.Maximum);
        target.Write(modulated);
    }

    private static void ApplyLegacyModulator(Track track, TrackModulator mod, double timeSec, double beat, double bpm,
        Project? project)
    {
        var target = ProjectFile.BuildTarget(track, (int)mod.Target.Kind,
            mod.Target.EffectIndex, mod.Target.ParamIndex, project);
        if (target is null) return;

        var current = target.Read();
        var depth = Math.Clamp(mod.Depth, 0, 1);
        double uni;

        switch (mod.Kind)
        {
            case TrackModulatorKind.EnvelopeFollower:
                uni = ModulatorEval.EnvelopeFollower(track, mod.Id, mod.AttackSeconds, mod.ReleaseSeconds, _envLevel);
                break;
            default:
            {
                var rateHz = mod.RateHz;
                if (mod.TempoSync && bpm > 0)
                    rateHz = bpm / 60.0 * Math.Max(1e-6, mod.RateHz);
                var phase = rateHz > 0 ? timeSec * rateHz : 0;
                phase -= Math.Floor(phase);
                uni = ModulatorEval.LfoUnipolar(mod.Wave, phase);
                break;
            }
        }

        var modulated = Blend(current, uni, depth, mod.Target.Kind, target.Minimum, target.Maximum);
        target.Write(modulated);
    }

    private static double Blend(double current, double uni, double depth, AutomationTargetKind kind,
        double min, double max)
    {
        var lfo = uni * 2.0 - 1.0;
        return kind switch
        {
            AutomationTargetKind.TrackVolume =>
                Math.Clamp(current * (1.0 - depth + depth * uni), min, max),
            AutomationTargetKind.TrackPan =>
                Math.Clamp(current + (lfo * depth * 0.5), min, max),
            AutomationTargetKind.TrackSendLevel =>
                Math.Clamp(current * (1.0 - depth + depth * uni), min, max),
            _ => Math.Clamp(current + (lfo * depth * (max - min) * 0.25), min, max)
        };
    }
}
