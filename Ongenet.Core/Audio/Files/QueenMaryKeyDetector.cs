using System;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Key detection using Mixxx's default Queen Mary <c>GetKeyMode</c> pipeline:
/// anti-aliased decimation, Constant-Q chromagram, HPCP averaging, Krumhansl profiles, median filter.
/// </summary>
internal static class QueenMaryKeyDetector
{
    private static readonly string[] PitchClasses =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static string Detect(AudioSampleBuffer buffer)
    {
        var sampleRate = buffer.SampleRate;
        var frames = buffer.FrameCount;
        if (sampleRate <= 0 || frames < sampleRate / 4) return string.Empty;

        var mono = Downmix(buffer);
        var energy = 0.0;
        foreach (var s in mono) energy += s * (double)s;
        if (energy <= 1e-6) return string.Empty;

        var keyMode = new QueenMaryGetKeyMode(sampleRate);
        if (mono.Length < keyMode.BlockSize) return string.Empty;

        var block = new float[keyMode.BlockSize];
        var lastKey = 0;
        for (var pos = 0; pos + keyMode.BlockSize <= mono.Length; pos += keyMode.HopSize)
        {
            Array.Copy(mono, pos, block, 0, keyMode.BlockSize);
            lastKey = keyMode.Process(block);
        }

        return KeyIndexToString(lastKey);
    }

    private static string KeyIndexToString(int keyIndex)
    {
        if (keyIndex <= 0 || keyIndex > 24) return string.Empty;
        var idx = keyIndex - 1;
        var minor = idx >= 12;
        var tonic = idx % 12;
        return $"{PitchClasses[tonic]} {(minor ? "min" : "maj")}";
    }

    private static float[] Downmix(AudioSampleBuffer buffer)
    {
        var frames = buffer.FrameCount;
        var channels = buffer.Channels;
        var mono = new float[frames];
        if (channels <= 1)
        {
            for (long f = 0; f < frames; f++)
                mono[f] = buffer.Sample(f, 0);
            return mono;
        }

        for (long f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
                sum += buffer.Sample(f, c);
            mono[f] = sum / channels;
        }

        return mono;
    }
}
