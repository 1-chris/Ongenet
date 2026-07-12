using System;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Models.Audio;

/// <summary>What kind of modulation source drives a <see cref="TrackModulator"/>.</summary>
public enum TrackModulatorKind
{
    Lfo = 0,
    EnvelopeFollower = 1
}

/// <summary>
/// A track-level modulator (e.g. an LFO) that targets a parameter binding and is evaluated at
/// schedule time alongside automation. Proof-of-concept: one LFO → track volume.
/// </summary>
public sealed class TrackModulator
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TrackModulatorKind Kind { get; set; } = TrackModulatorKind.Lfo;

    public bool Enabled { get; set; } = true;

    /// <summary>LFO rate in Hz when <see cref="TempoSync"/> is false.</summary>
    public double RateHz { get; set; } = 0.25;

    /// <summary>When true, <see cref="RateHz"/> is interpreted as rate in bars (e.g. 0.25 = 1/4 note).</summary>
    public bool TempoSync { get; set; }

    /// <summary>Modulation depth, 0..1.</summary>
    public double Depth { get; set; } = 0.5;

    public LfoWave Wave { get; set; } = LfoWave.Sine;

    /// <summary>Envelope follower attack/release (seconds) when <see cref="Kind"/> is EnvelopeFollower.</summary>
    public double AttackSeconds { get; set; } = 0.01;
    public double ReleaseSeconds { get; set; } = 0.2;

    /// <summary>Which parameter this modulator drives (re-bound on project load).</summary>
    public AutomationBinding Target { get; set; } =
        new(AutomationTargetKind.TrackVolume, -1, -1);
}
