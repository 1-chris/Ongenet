using System;

namespace Ongenet.Core.Audio.Midi;

/// <summary>Beat-grid quantization helpers for recorded MIDI.</summary>
public static class MidiQuantize
{
    /// <summary>
    /// Snaps <paramref name="beat"/> to the nearest multiple of <paramref name="gridBeats"/>.
    /// A non-positive grid leaves the value unchanged (quantize off). E.g. a 1/16 grid in 4/4 is
    /// 0.25 beats, so beat 1.31 snaps to 1.25.
    /// </summary>
    public static double Snap(double beat, double gridBeats)
        => gridBeats <= 0 ? beat : Math.Round(beat / gridBeats) * gridBeats;
}
