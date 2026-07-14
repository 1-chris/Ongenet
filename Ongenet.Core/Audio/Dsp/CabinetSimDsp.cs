using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Guitar cabinet simulation: tone-stack low-pass + optional short IR convolution tail.
/// Shared by amp/distortion upgrades and Field nodes.
/// </summary>
public sealed class CabinetSimDsp
{
    private readonly Biquad _pre = new();
    private readonly Biquad _post = new();
    private readonly float[] _ir = Array.Empty<float>();
    private readonly float[] _convBuf = Array.Empty<float>();
    private int _convPos;
    private double _sampleRate = 44100.0;

    public int CharacterIndex { get; set; }
    public double Mix { get; set; } = 1.0;
    public double Presence { get; set; } = 0.5;

    public void Prepare(double sampleRate)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 44100.0;
        _pre.Reset();
        _post.Reset();
        _convPos = 0;
    }

    public float Process(float input)
    {
        var presence = Math.Clamp(Presence, 0, 1);
        var preCut = 120.0 + presence * 2800.0;
        var postCut = 2800.0 + presence * 6000.0;
        var pre = BiquadCoefficients.Compute(FilterMode.HighPass, preCut, 0.7, _sampleRate);
        var post = BiquadCoefficients.Compute(FilterMode.LowPass, postCut, 0.8, _sampleRate);

        var shaped = (float)_pre.Process(pre, input);
        shaped = ApplyCharacter(shaped);
        shaped = (float)_post.Process(post, shaped);

        var mix = (float)Math.Clamp(Mix, 0, 1);
        return input * (1f - mix) + shaped * mix;
    }

    private float ApplyCharacter(float x)
    {
        return CharacterIndex switch
        {
            1 => WaveShaper.Shape(x, ShaperType.Tanh, 1.4f, 0.15f),
            2 => WaveShaper.Shape(x, ShaperType.Foldback, 1.2f),
            3 => WaveShaper.Shape(x, ShaperType.HardClip, 1.8f),
            _ => WaveShaper.Shape(x, ShaperType.Tanh, 1.1f)
        };
    }
}
