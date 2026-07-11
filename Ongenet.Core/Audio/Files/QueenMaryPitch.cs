using System;

namespace Ongenet.Core.Audio.Files;

/// <summary>MIDI pitch helpers from qm-dsp <c>Pitch</c>.</summary>
internal static class QueenMaryPitch
{
    public static double FrequencyForMidi(int midiPitch, double centsOffset = 0, double concertA = 440.0)
    {
        var p = midiPitch + centsOffset / 100.0;
        return concertA * Math.Pow(2.0, (p - 69.0) / 12.0);
    }
}
