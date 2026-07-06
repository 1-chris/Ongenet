using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>Geometric (and noise / custom-wavetable) waveforms for an audio-rate oscillator.</summary>
public enum OscWave
{
    Sine,
    Triangle,
    Saw,
    Square,
    Noise,

    /// <summary>Reads <see cref="WaveOscillator.CustomTable"/> as a single repeating cycle.</summary>
    Custom
}

/// <summary>
/// A reusable audio-rate oscillator: phase accumulator with selectable waveform, a static phase offset,
/// optional phase invert, white-noise mode (its own <see cref="FastRandom"/>), and a custom wavetable.
/// The saw and square are band-limited with PolyBLEP so they stay smooth and alias-free (the difference
/// between a "primitive" buzzy saw and a modern soft-synth supersaw). Reusable by any synth/effect.
/// </summary>
public sealed class WaveOscillator
{
    private double _phase;          // [0,1)
    private double _inc;            // phase per sample
    private int _sampleRate = 44100;
    private FastRandom _rng = new(0x1234567u);

    public OscWave Wave { get; set; } = OscWave.Sine;

    /// <summary>Static offset added to the phase before shaping, in cycles [0,1).</summary>
    public double PhaseOffset { get; set; }

    /// <summary>Flips the output's polarity.</summary>
    public bool Invert { get; set; }

    /// <summary>Single-cycle table used when <see cref="Wave"/> is <see cref="OscWave.Custom"/>.</summary>
    public float[]? CustomTable { get; set; }

    public void SetSampleRate(int sampleRate) => _sampleRate = sampleRate <= 0 ? 44100 : sampleRate;

    public void SetFrequency(double hz) => _inc = hz / _sampleRate;

    public void ResetPhase(double phase = 0.0) => _phase = phase - Math.Floor(phase);

    /// <summary>Seeds the noise generator (per voice, so layered noise oscillators decorrelate).</summary>
    public void SeedNoise(uint seed) => _rng = new FastRandom(seed);

    /// <summary>Produces the next sample in [-1, 1] and advances the phase.</summary>
    public float Next()
    {
        var p = _phase + PhaseOffset;
        p -= Math.Floor(p);
        var dt = _inc <= 0 ? 0.0 : (_inc >= 0.5 ? 0.5 : _inc);

        double value;
        switch (Wave)
        {
            case OscWave.Triangle:
                value = 1.0 - 4.0 * Math.Abs(p - 0.5);
                break;
            case OscWave.Saw:
                // Band-limited: correct the ramp discontinuity at the wrap point.
                value = 2.0 * p - 1.0 - PolyBlep(p, dt);
                break;
            case OscWave.Square:
            {
                value = p < 0.5 ? 1.0 : -1.0;
                value += PolyBlep(p, dt);            // rising edge at 0
                var pHalf = p + 0.5;
                pHalf -= Math.Floor(pHalf);
                value -= PolyBlep(pHalf, dt);        // falling edge at 0.5
                break;
            }
            case OscWave.Noise:
                value = _rng.NextBipolar();
                break;
            case OscWave.Custom:
                value = ReadCustom(p);
                break;
            default:
                value = Math.Sin(p * 2.0 * Math.PI);
                break;
        }

        _phase += _inc;
        if (_phase >= 1.0) _phase -= 1.0;

        return (float)(Invert ? -value : value);
    }

    // PolyBLEP residual: smooths the one-sample step at a waveform discontinuity to kill aliasing.
    private static double PolyBlep(double t, double dt)
    {
        if (dt <= 0.0) return 0.0;
        if (t < dt)
        {
            t /= dt;
            return t + t - t * t - 1.0;
        }

        if (t > 1.0 - dt)
        {
            t = (t - 1.0) / dt;
            return t * t + t + t + 1.0;
        }

        return 0.0;
    }

    private double ReadCustom(double p)
    {
        var t = CustomTable;
        if (t is null || t.Length < 2) return 0.0;
        var x = p * (t.Length - 1);
        var i0 = (int)x;
        var frac = x - i0;
        var i1 = i0 + 1 >= t.Length ? t.Length - 1 : i0 + 1;
        return t[i0] + (t[i1] - t[i0]) * frac;
    }
}
