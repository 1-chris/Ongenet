using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>Factory modulator slot chains materialized as loadable modulator presets.</summary>
public static class FactoryModulatorPresets
{
    public sealed record Definition(string PresetName, Func<IReadOnlyList<ModulatorSlot>> Create, string[] Tags);

    public static IReadOnlyList<Definition> Definitions { get; } =
    [
        new("Peak Controller", PeakController, ["dynamics", "sidechain", "macro"]),
        new("XY Performance", XyPerformance, ["performance", "xy", "macro"]),
        new("Formula Swell", FormulaSwell, ["math", "lfo", "creative"]),
    ];

    /// <summary>Envelope follower ducking track volume (FL Peak Controller-style).</summary>
    private static IReadOnlyList<ModulatorSlot> PeakController() =>
    [
        new ModulatorSlot
        {
            Depth = 0.85,
            Source = new EnvelopeFollowerModulator { Attack = 0.005, Release = 0.18 },
            Target = new AutomationBinding(AutomationTargetKind.TrackVolume, -1, -1)
        },
        new ModulatorSlot
        {
            Depth = 0.6,
            Source = new MacroModulator { Value = 0.35 },
            Target = new AutomationBinding(AutomationTargetKind.TrackVolume, -1, -1)
        }
    ];

    /// <summary>XY pad blended into track pan for live performance morphing.</summary>
    private static IReadOnlyList<ModulatorSlot> XyPerformance() =>
    [
        new ModulatorSlot
        {
            Depth = 0.75,
            Source = new XyModulator { X = 0.35, Y = 0.65 },
            Target = new AutomationBinding(AutomationTargetKind.TrackPan, -1, -1)
        },
        new ModulatorSlot
        {
            Depth = 0.5,
            Source = new XyModulator { X = 0.65, Y = 0.35 },
            Target = new AutomationBinding(AutomationTargetKind.TrackVolume, -1, -1)
        }
    ];

    /// <summary>Math modulator shaping an LFO-driven filter macro curve.</summary>
    private static IReadOnlyList<ModulatorSlot> FormulaSwell() =>
    [
        new ModulatorSlot
        {
            Depth = 0.7,
            Source = new MathModulator { A = 0.5, B = 0.25, Op = 2 },
            Target = new AutomationBinding(AutomationTargetKind.TrackVolume, -1, -1)
        },
        new ModulatorSlot
        {
            Depth = 0.55,
            Source = new LfoModulator { Rate = 0.125, TempoSync = true, Wave = LfoWave.Sine },
            Target = new AutomationBinding(AutomationTargetKind.TrackPan, -1, -1)
        }
    ];
}
