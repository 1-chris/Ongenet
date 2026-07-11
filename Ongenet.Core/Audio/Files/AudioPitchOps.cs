using System;
using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio.Files;

/// <summary>Offline pitch processing for sample buffers (duration-preserving).</summary>
public static class AudioPitchOps
{
    /// <summary>
    /// Returns a copy of <paramref name="buffer"/> pitch-shifted by <paramref name="semitones"/> without
    /// changing its length or sample rate. Pure .NET port of Rubber Band R2 offline pitch shift
    /// (study pass, laminar phase linking, adaptive increments, overlap-add, Hermite resample).
    /// </summary>
    public static AudioSampleBuffer PitchShift(AudioSampleBuffer buffer, double semitones,
        IProgress<double>? progress = null)
    {
        if (buffer.FrameCount <= 0 || Math.Abs(semitones) < 1e-6)
        {
            progress?.Report(1.0);
            return buffer;
        }

        var shifted = ChangePitchProcessor.Shift(buffer.Samples, buffer.Channels, buffer.SampleRate,
            semitones, progress);
        return new AudioSampleBuffer(shifted, buffer.Channels, buffer.SampleRate);
    }
}
