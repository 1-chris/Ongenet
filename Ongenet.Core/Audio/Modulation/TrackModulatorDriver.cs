using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>
/// Evaluates <see cref="TrackModulator"/> sources at schedule time and writes into their bound
/// targets. Called after automation each block so modulators layer on top of automated values.
/// </summary>
public static class TrackModulatorDriver
{
    private static readonly Dictionary<Guid, float> _envLevel = new();

    public static void ApplyTrack(Track track, double beat, double bpm, Project? project = null)
    {
        var mods = track.ActiveModulators;
        if (mods.Length == 0) return;

        var timeSec = beat * 60.0 / (bpm > 0 ? bpm : 120.0);
        foreach (var mod in mods)
        {
            if (!mod.Enabled) continue;
            ApplyModulator(track, mod, timeSec, beat, bpm, project);
        }
    }

    private static void ApplyModulator(Track track, TrackModulator mod, double timeSec, double beat, double bpm,
        Project? project)
    {
        var target = ProjectFile.BuildTarget(track, (int)mod.Target.Kind,
            mod.Target.EffectIndex, mod.Target.ParamIndex, project);
        if (target is null) return;

        var current = target.Read();
        var depth = Math.Clamp(mod.Depth, 0, 1);
        double modulated;

        switch (mod.Kind)
        {
            case TrackModulatorKind.EnvelopeFollower:
            {
                var level = _envLevel.GetValueOrDefault(track.Id);
                var input = Math.Clamp(track.MeterLevel, 0f, 1f);
                var atk = Math.Max(1e-4, mod.AttackSeconds);
                var rel = Math.Max(1e-4, mod.ReleaseSeconds);
                var blockSec = 512.0 / 48000.0;
                var coeff = input > level
                    ? 1.0 - Math.Exp(-blockSec / atk)
                    : 1.0 - Math.Exp(-blockSec / rel);
                level = (float)(level + (input - level) * coeff);
                _envLevel[track.Id] = level;
                modulated = Math.Clamp(current * (1.0 - depth + depth * level), target.Minimum, target.Maximum);
                break;
            }
            default:
            {
                var rateHz = mod.RateHz;
                if (mod.TempoSync && bpm > 0)
                    rateHz = bpm / 60.0 * Math.Max(1e-6, mod.RateHz);

                var phase = rateHz > 0 ? timeSec * rateHz : 0;
                phase -= Math.Floor(phase);
                var lfo = Lfo.Evaluate(mod.Wave, phase);
                var uni = (lfo + 1.0) * 0.5;
                modulated = mod.Target.Kind switch
                {
                    AutomationTargetKind.TrackVolume =>
                        Math.Clamp(current * (1.0 - depth + depth * uni), target.Minimum, target.Maximum),
                    AutomationTargetKind.TrackPan =>
                        Math.Clamp(current + (lfo * depth * 0.5), target.Minimum, target.Maximum),
                    AutomationTargetKind.TrackSendLevel =>
                        Math.Clamp(current * (1.0 - depth + depth * uni), target.Minimum, target.Maximum),
                    _ => Math.Clamp(current + (lfo * depth * (target.Maximum - target.Minimum) * 0.25),
                        target.Minimum, target.Maximum)
                };
                break;
            }
        }

        target.Write(modulated);
    }
}
