using System;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>The built-in procedural wavetable shapes the synth can generate.</summary>
public enum WavetablePreset
{
    /// <summary>Morphs sine → triangle → saw → square across the scan.</summary>
    Basic,

    /// <summary>A harmonic sweep: a single sine, gaining harmonics toward a full sawtooth as you scan up.</summary>
    Harmonics,

    /// <summary>An evolving random-spectrum table (different every time you press Random).</summary>
    Random
}

/// <summary>
/// Builds <see cref="Wavetable"/>s — either procedurally (the built-in presets) or by slicing a loaded
/// audio sample into equal-length frames (the "import a sample as a wavetable" workflow). Reusable.
/// </summary>
public static class WavetableGenerator
{
    public const int DefaultFrameCount = 64;
    public const int DefaultFrameLength = 2048;

    /// <summary>Generates a procedural table. <paramref name="seed"/> only matters for <see cref="WavetablePreset.Random"/>.</summary>
    public static Wavetable BuildPreset(WavetablePreset preset, int frameCount = DefaultFrameCount,
        int frameLength = DefaultFrameLength, int seed = 0)
    {
        var data = new float[frameCount * frameLength];
        switch (preset)
        {
            case WavetablePreset.Harmonics: BuildHarmonics(data, frameCount, frameLength); break;
            case WavetablePreset.Random: BuildRandom(data, frameCount, frameLength, seed); break;
            default: BuildBasic(data, frameCount, frameLength); break;
        }

        for (var f = 0; f < frameCount; f++) NormalizeFrame(data, f * frameLength, frameLength);
        return new Wavetable(data, frameCount, frameLength);
    }

    /// <summary>
    /// Slices a sample into <paramref name="frameLength"/>-sample frames (up to <paramref name="maxFrames"/>),
    /// each normalised — scanning then sweeps through the sample's evolving timbre.
    /// </summary>
    public static Wavetable FromSample(AudioSampleBuffer sample, int frameLength = DefaultFrameLength, int maxFrames = 256)
    {
        var mono = SampleMixdown.ToMono(sample, guard: false);
        var available = Math.Max(1, mono.Length / frameLength);
        var frameCount = Math.Clamp(available, 1, maxFrames);

        var data = new float[frameCount * frameLength];
        for (var f = 0; f < frameCount; f++)
        {
            var src = f * frameLength;
            var dst = f * frameLength;
            for (var i = 0; i < frameLength; i++)
            {
                var s = src + i;
                data[dst + i] = s < mono.Length ? mono[s] : 0f;
            }

            NormalizeFrame(data, dst, frameLength);
        }

        return new Wavetable(data, frameCount, frameLength);
    }

    private static void BuildBasic(float[] data, int frameCount, int frameLength)
    {
        // Three morph segments across the scan: sine→triangle→saw→square.
        for (var f = 0; f < frameCount; f++)
        {
            var s = frameCount > 1 ? (float)f / (frameCount - 1) : 0f;
            var seg = s * 3f;
            var which = Math.Min(2, (int)seg);
            var blend = seg - which;
            var baseI = f * frameLength;
            for (var i = 0; i < frameLength; i++)
            {
                var t = (float)i / frameLength;
                var a = Shape(which, t);
                var b = Shape(which + 1, t);
                data[baseI + i] = a + (b - a) * blend;
            }
        }
    }

    // 0=sine, 1=triangle, 2=saw, 3=square.
    private static float Shape(int kind, float t) => kind switch
    {
        1 => 1f - 4f * MathF.Abs(t - 0.5f),
        2 => 2f * t - 1f,
        3 => t < 0.5f ? 1f : -1f,
        _ => MathF.Sin(2f * MathF.PI * t)
    };

    private static void BuildHarmonics(float[] data, int frameCount, int frameLength)
    {
        const int maxHarmonics = 48;
        for (var f = 0; f < frameCount; f++)
        {
            var s = frameCount > 1 ? (float)f / (frameCount - 1) : 0f;
            var harmonics = 1 + (int)MathF.Round(s * (maxHarmonics - 1));
            var baseI = f * frameLength;
            for (var i = 0; i < frameLength; i++)
            {
                var t = 2f * MathF.PI * i / frameLength;
                var sum = 0f;
                for (var k = 1; k <= harmonics; k++) sum += MathF.Sin(k * t) / k; // sawtooth partials
                data[baseI + i] = sum;
            }
        }
    }

    private static void BuildRandom(float[] data, int frameCount, int frameLength, int seed)
    {
        const int harmonics = 24;
        var rng = new Random(seed == 0 ? Environment.TickCount : seed);
        // Per-harmonic phase + a slow modulation rate so the spectrum evolves smoothly across frames.
        var phase = new float[harmonics + 1];
        var rate = new float[harmonics + 1];
        var weight = new float[harmonics + 1];
        for (var k = 1; k <= harmonics; k++)
        {
            phase[k] = (float)(rng.NextDouble() * MathF.PI * 2);
            rate[k] = (float)(rng.NextDouble() * 3.0);
            weight[k] = (float)(0.3 + rng.NextDouble() * 0.7) / k; // gentle rolloff
        }

        for (var f = 0; f < frameCount; f++)
        {
            var s = frameCount > 1 ? (float)f / (frameCount - 1) : 0f;
            var baseI = f * frameLength;
            for (var i = 0; i < frameLength; i++)
            {
                var t = 2f * MathF.PI * i / frameLength;
                var sum = 0f;
                for (var k = 1; k <= harmonics; k++)
                {
                    var amp = weight[k] * (0.5f + 0.5f * MathF.Sin(phase[k] + s * rate[k] * MathF.PI * 2));
                    sum += amp * MathF.Sin(k * t + phase[k]);
                }

                data[baseI + i] = sum;
            }
        }
    }

    private static void NormalizeFrame(float[] data, int offset, int length)
    {
        var peak = 0f;
        for (var i = 0; i < length; i++)
        {
            var a = MathF.Abs(data[offset + i]);
            if (a > peak) peak = a;
        }

        if (peak < 1e-6f) return;
        var gain = 0.9f / peak;
        for (var i = 0; i < length; i++) data[offset + i] *= gain;
    }
}
