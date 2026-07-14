using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Containers;

/// <summary>Code-defined factory presets for container effects (FX Layer, Multiband FX, etc.).</summary>
public static class FactoryContainerPresets
{
    public sealed record Definition(string EffectDisplayName, string PresetName, Func<IAudioEffect> Create, string[] Tags);

    public static IReadOnlyList<Definition> Definitions { get; } =
    [
        new("FX Layer", "Distructor", DistructorFxLayer, ["distortion", "amp", "filter", "guitar"]),
        new("FX Layer", "Effector Multi-Mod", EffectorMultiMod, ["modulation", "chorus", "phaser", "flanger"]),
        new("Multiband FX-3", "Tri-Band Delays", TriBandDelays, ["delay", "multiband", "spatial"]),
    ];

    private static IAudioEffect DistructorFxLayer() => FxLayerEffect.FromChains(
    [
        [
            new DistortionEffect { DriveDb = 18, Mix = 1.0, Mode = 1 },
            new FilterEffect { Mode = FilterMode.LowPass, Frequency = 2200, Resonance = 1.2 },
            new AmpEffect { Drive = 10, Tone = 0.55, Mix = 0.9, CabCharacter = 3, CabMix = 0.65 }
        ],
        [
            new FilterEffect { Mode = FilterMode.HighPass, Frequency = 160 },
            new AmpEffect { Drive = 4, Tone = 0.45, Mix = 0.55, CabCharacter = 1, CabMix = 0.4 }
        ]
    ]);

    private static IAudioEffect EffectorMultiMod() => FxLayerEffect.FromChains(
    [
        [new ChorusEffect { RateHz = 0.35, Depth = 0.45, Mix = 0.55 }],
        [new PhaserEffect { RateHz = 0.22, Depth = 0.6, Mix = 0.5 }],
        [new FlangerEffect { RateHz = 0.18, Depth = 0.7, Feedback = 0.35, Mix = 0.45 }]
    ]);

    private static IAudioEffect TriBandDelays() => MultibandFxEffect.FromBands(3, 280, 2800,
    [
        [new DelayEffect { TimeMs = 520, Feedback = 0.42, Mix = 0.38 }],
        [new DelayEffect { TimeMs = 340, Feedback = 0.36, Mix = 0.32, PingPong = true }],
        [new DelayEffect { TimeMs = 180, Feedback = 0.28, Mix = 0.28 }]
    ]);
}
