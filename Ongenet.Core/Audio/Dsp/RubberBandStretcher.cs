using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Duration-preserving pitch shift using a pure .NET port of Rubber Band R2 offline processing
/// (study pass, laminar phase linking, adaptive chunk increments, Hann OLA, Hermite resample).
/// </summary>
internal static class RubberBandStretcher
{
    public static float[] Shift(float[] input, int channels, int sampleRate, double semitones,
        IProgress<double>? progress = null)
    {
        var frames = input.Length / channels;
        if (frames <= 0 || Math.Abs(semitones) < 1e-6)
        {
            progress?.Report(1.0);
            return input;
        }

        var pitchScale = MusicalMath.SemitonesToRatio(semitones);
        var output = new float[frames * channels];

        for (var c = 0; c < channels; c++)
        {
            var channelIn = Deinterleave(input, channels, frames, c);
            var config = RubberBandR2Config.Create(sampleRate, pitchScale, 1.0, channelIn.Length);
            var engine = new RubberBandR2Engine(config);
            var channelOut = engine.PitchShift(channelIn, progress, c, channels);
            if (channelOut.Length != channelIn.Length)
                channelOut = FitToLength(channelOut, channelIn.Length);
            Interleave(output, channels, channelOut, c);
        }

        progress?.Report(1.0);
        return output;
    }

    /// <summary>Duration-preserving time stretch (unity pitch) for warp Complex segments.</summary>
    public static float[] TimeStretch(float[] input, int channels, int sampleRate, double timeRatio,
        IProgress<double>? progress = null)
    {
        var frames = input.Length / channels;
        if (frames <= 0 || Math.Abs(timeRatio - 1.0) < 1e-6)
        {
            progress?.Report(1.0);
            return input;
        }

        var output = new float[frames * channels];
        for (var c = 0; c < channels; c++)
        {
            var channelIn = Deinterleave(input, channels, frames, c);
            var config = RubberBandR2Config.Create(sampleRate, 1.0, timeRatio, channelIn.Length);
            var engine = new RubberBandR2Engine(config);
            var channelOut = engine.PitchShift(channelIn, progress, c, channels);
            var targetLen = Math.Max(1, (int)Math.Round(channelIn.Length * timeRatio));
            if (channelOut.Length != targetLen)
                channelOut = FitToLength(channelOut, targetLen);
            if (channelOut.Length != channelIn.Length)
                channelOut = FitToLength(channelOut, channelIn.Length);
            Interleave(output, channels, channelOut, c);
        }

        progress?.Report(1.0);
        return output;
    }

    private static float[] Deinterleave(float[] interleaved, int channels, long frames, int channel)
    {
        var mono = new float[frames];
        for (long f = 0; f < frames; f++)
            mono[f] = interleaved[f * channels + channel];
        return mono;
    }

    private static void Interleave(float[] interleaved, int channels, float[] channel, int channelIndex)
    {
        var frames = channel.Length;
        for (long f = 0; f < frames; f++)
            interleaved[f * channels + channelIndex] = channel[f];
    }

    private static float[] FitToLength(float[] input, int targetLength)
    {
        if (input.Length == targetLength) return input;
        var result = new float[targetLength];
        if (input.Length <= 0) return result;
        if (input.Length > targetLength)
        {
            Array.Copy(input, 0, result, 0, targetLength);
            return result;
        }

        Array.Copy(input, 0, result, 0, input.Length);
        result.AsSpan(input.Length).Fill(input[^1]);
        return result;
    }
}
