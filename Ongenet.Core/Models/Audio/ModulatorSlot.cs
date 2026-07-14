using System;
using Ongenet.Core.Audio.Automation;
using Ongenet.Core.Audio.Modulation;

namespace Ongenet.Core.Models.Audio;

/// <summary>
/// A registry-backed modulator on a track: source device + depth + parameter target.
/// Mirrors the MIDI-FX slot pattern (<see cref="Track.MidiEffects"/>).
/// </summary>
public sealed class ModulatorSlot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public double Depth { get; set; } = 0.5;
    public IModulator Source { get; set; } = new LfoModulator();
    public AutomationBinding Target { get; set; } =
        new(AutomationTargetKind.TrackVolume, -1, -1);
}
