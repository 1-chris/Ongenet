using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Offline pitch shift — delegates to the pure .NET Rubber Band R2 engine.
/// </summary>
internal static class ChangePitchProcessor
{
    public static float[] Shift(float[] input, int channels, int sampleRate, double semitones,
        IProgress<double>? progress = null)
        => RubberBandStretcher.Shift(input, channels, sampleRate, semitones, progress);
}
