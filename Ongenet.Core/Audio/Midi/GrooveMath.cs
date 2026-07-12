using System;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Midi;

/// <summary>Shared groove/swing timing applied by arrangement and pattern schedulers.</summary>
public static class GrooveMath
{
    public static double Apply(double beat, GrooveTemplate? groove)
    {
        if (groove is null || groove.Division <= 0) return beat;
        var stepBeats = 4.0 / groove.Division;
        if (stepBeats <= 1e-9) return beat;
        var stepIndex = (int)(Math.Floor(beat / stepBeats) % groove.Division);
        if (stepIndex >= 0 && stepIndex < groove.StepOffsets.Count && Math.Abs(groove.StepOffsets[stepIndex]) > 1e-9)
            return beat + groove.StepOffsets[stepIndex];
        var idx = (long)Math.Floor(beat / stepBeats);
        if (idx % 2 != 1) return beat;
        var swingOffset = stepBeats * (groove.SwingAmount - 0.5) * 2.0;
        return beat + swingOffset;
    }
}
