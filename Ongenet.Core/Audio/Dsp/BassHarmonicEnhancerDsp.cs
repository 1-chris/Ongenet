using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Generates upper harmonics on low-frequency content so bass translates on small speakers.
/// Used by exciter upgrade and optional bass synth sub enhancement.
/// </summary>
public sealed class BassHarmonicEnhancerDsp
{
    private readonly Biquad _low = new();
    private readonly Biquad _high = new();
    private double _sampleRate = 44100.0;

    public double Amount { get; set; } = 0.5;
    public double Frequency { get; set; } = 120.0;
    public double Drive { get; set; } = 2.0;

    public void Prepare(double sampleRate)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 44100.0;
        _low.Reset();
        _high.Reset();
    }

    public float Process(float input)
    {
        var freq = Math.Clamp(Frequency, 40, 400);
        var lp = BiquadCoefficients.Compute(FilterMode.LowPass, freq, 0.7, _sampleRate);
        var hp = BiquadCoefficients.Compute(FilterMode.HighPass, freq * 1.5, 0.7, _sampleRate);
        var low = (float)_low.Process(lp, input);
        var harmonics = WaveShaper.Shape(low, ShaperType.Tanh, (float)Math.Max(1, Drive));
        harmonics = (float)_high.Process(hp, harmonics);
        var amt = (float)Math.Clamp(Amount, 0, 1);
        return input + harmonics * amt;
    }
}
