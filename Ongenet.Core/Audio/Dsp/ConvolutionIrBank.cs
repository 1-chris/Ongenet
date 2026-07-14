using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>Factory impulse presets for convolution reverb (synthetic mono IRs).</summary>
public static class ConvolutionIrBank
{
    public static readonly string[] PresetNames = { "Room", "Hall", "Plate", "Chamber", "Large Hall" };

    public static float[] BuildSyntheticIr(double sampleRate, int presetIndex, double decaySeconds, double size)
    {
        var preset = ReverbAlgorithmBank.Get(presetIndex);
        var decay = decaySeconds > 0 ? decaySeconds : preset.RoomSize * 3.0;
        var len = (int)Math.Clamp(sampleRate * decay * (0.5 + size), 2048, sampleRate * 4);
        var ir = new float[len];
        var rng = new FastRandom(0xC0FFEEu + (uint)presetIndex);
        for (var i = 0; i < len; i++)
        {
            var t = i / sampleRate;
            var env = Math.Exp(-3.5 * t / decay) * (1.0 + preset.ModDepth * Math.Sin(t * 12.0));
            ir[i] = (float)(rng.NextBipolar() * env * (0.4 + preset.RoomSize * 0.6));
        }
        var peak = 0f;
        foreach (var s in ir) peak = Math.Max(peak, Math.Abs(s));
        if (peak > 1e-6f)
            for (var i = 0; i < len; i++) ir[i] /= peak;
        return ir;
    }
}
