using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A classic ring modulator: multiplies the input (the carrier) by an internal sine oscillator, so the
/// output contains only the sum and difference frequencies (input ± modulator) — the metallic, bell-like,
/// inharmonic timbre of Dalek voices and clangorous FX. A <see cref="Mix"/> control blends the ring
/// product back with the dry signal. Mono, allocation- and branch-light in <see cref="Process"/>; holds
/// its own phase so it can be swept continuously. Reusable by any instrument/effect.
/// </summary>
public sealed class RingModulator
{
    private double _phase;   // [0,1)
    private double _inc;     // cycles per sample
    private float _mix = 1f;

    /// <summary>Wet/dry blend: 0 = dry input, 1 = pure ring product.</summary>
    public float Mix
    {
        get => _mix;
        set => _mix = AudioMath.Clamp(value, 0f, 1f);
    }

    public void Configure(double freqHz, int sampleRate)
        => _inc = sampleRate > 0 ? Math.Max(0.0, freqHz) / sampleRate : 0.0;

    public void Reset(double phase = 0.0) => _phase = phase - Math.Floor(phase);

    /// <summary>Multiplies one input sample by the sine carrier and advances the phase.</summary>
    public float Process(float sample)
    {
        var carrier = (float)Math.Sin(_phase * 2.0 * Math.PI);
        _phase += _inc;
        if (_phase >= 1.0) _phase -= 1.0;

        var ring = sample * carrier;
        return sample + (ring - sample) * _mix;
    }
}
