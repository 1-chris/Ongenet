using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>Dither modes for PCM quantisation.</summary>
public enum DitherMode
{
    /// <summary>Triangular PDF white dither (default).</summary>
    Tpdf = 0,
    /// <summary>First-order noise-shaped TPDF (pushes dither energy out of low mids).</summary>
    NoiseShaped = 1
}

/// <summary>TPDF / noise-shaped dither for PCM quantisation. Stateful, allocation-free.</summary>
public sealed class PcmDither
{
    private uint _state = 0xA5A5A5A5;
    private float _error;
    public DitherMode Mode { get; set; } = DitherMode.Tpdf;

    public void Reset(uint seed = 0xA5A5A5A5)
    {
        _state = seed == 0 ? 1u : seed;
        _error = 0;
    }

    /// <summary>
    /// Applies dither scaled for <paramref name="bitsPerSample"/> quantisation, then returns the
    /// dithered sample still in float −1..1 range (caller quantises).
    /// </summary>
    public float Process(float sample, int bitsPerSample)
    {
        var lsb = bitsPerSample switch
        {
            24 => 1f / 8388608f,
            32 => 0f,
            _ => 1f / 32768f
        };
        if (lsb <= 0) return sample;
        var tpdf = (NextUniform() - NextUniform()) * lsb;
        if (Mode == DitherMode.NoiseShaped)
        {
            // First-order feedback: high-pass the quantisation residual toward ultrasonic.
            var shaped = sample + tpdf - _error * 0.5f;
            _error = shaped - sample; // approximate residual before clamp/quantise
            return shaped;
        }
        return sample + tpdf;
    }

    private float NextUniform()
    {
        _state = _state * 1664525u + 1013904223u;
        return (_state >> 8) * (1f / 16777216f);
    }
}
