using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A serial chain of Schroeder all-pass sections — the diffusion building block of reverbs and "blur"
/// effects. Each stage passes all frequencies at equal gain but smears their phase, so transients get
/// progressively scattered into a dense, echo-free wash without colouring the spectrum. The delay times
/// are mutually detuned (near-prime ratios) so their resonances don't stack into an audible pitch.
/// Delay lines are sized once in <see cref="Configure"/>; <see cref="Process"/> is allocation-free.
/// Hold one per channel. Reusable by any reverb/space/blur effect.
/// </summary>
public sealed class AllpassDiffuser
{
    private const int Stages = 4;
    private const double MaxDelayMs = 80.0;

    // Detuned base delay times (ms) for each stage at full size.
    private static readonly double[] BaseMs = { 13.6, 21.3, 37.9, 59.7 };

    private readonly DelayLine[] _delays = new DelayLine[Stages];
    private readonly double[] _delaySamples = new double[Stages];
    private float _g;

    public AllpassDiffuser()
    {
        for (var i = 0; i < Stages; i++) _delays[i] = new DelayLine();
    }

    /// <summary>
    /// <paramref name="size"/> (0..1) scales the diffusion delay times; <paramref name="feedback"/>
    /// (0..~0.9) is the all-pass coefficient (higher = longer, denser smear).
    /// </summary>
    public void Configure(double size, double feedback, int sampleRate)
    {
        var sr = sampleRate > 0 ? sampleRate : 44100;
        var capacity = (int)(MaxDelayMs / 1000.0 * sr) + 8;
        var scale = AudioMath.Clamp(size, 0.05, 1.0);
        _g = (float)AudioMath.Clamp(feedback, 0.0, 0.9);

        for (var i = 0; i < Stages; i++)
        {
            if (_delays[i].Size < capacity) _delays[i].Resize(capacity);
            var ms = BaseMs[i] * scale;
            _delaySamples[i] = Math.Max(1.0, ms / 1000.0 * sr);
        }
    }

    public void Reset()
    {
        for (var i = 0; i < Stages; i++) _delays[i].Clear();
    }

    /// <summary>Diffuses one sample.</summary>
    public float Process(float sample)
    {
        var x = sample;
        for (var i = 0; i < Stages; i++)
        {
            var delayed = _delays[i].ReadFrac(_delaySamples[i]);
            var v = x + _g * delayed;   // feedback into the delay
            _delays[i].Write(v);
            x = delayed - _g * v;       // feed-forward → all-pass response
        }

        return x;
    }
}
