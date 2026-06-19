using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A slow, smooth random modulation source for "analog" drift — sample-and-hold targets joined by
/// smoothstep interpolation, producing a wandering signal in [-1, 1]. Cheaper and gentler than an
/// LFO for subtle pitch/filter wobble that keeps layered voices alive. Allocation-free; seed per
/// voice/oscillator for independent streams. Reusable by any instrument.
/// </summary>
public sealed class DriftGenerator
{
    private FastRandom _rng = new(1);
    private double _phase;
    private double _inc;
    private double _prev;
    private double _target;

    /// <summary>
    /// Sets the wander rate and reseeds. A low <paramref name="rateHz"/> (e.g. 0.05–1 Hz) gives slow drift.
    /// </summary>
    public void Configure(double rateHz, double sampleRate, uint seed)
    {
        _rng = new FastRandom(seed == 0 ? 1u : seed);
        _inc = sampleRate > 0 ? Math.Max(0.0, rateHz) / sampleRate : 0.0;
        _phase = 0;
        _prev = _rng.NextBipolar();
        _target = _rng.NextBipolar();
    }

    /// <summary>Advances by one sample and returns the current drift value in [-1, 1].</summary>
    public double Next()
    {
        _phase += _inc;
        if (_phase >= 1.0)
        {
            _phase -= Math.Floor(_phase);
            _prev = _target;
            _target = _rng.NextBipolar();
        }

        var t = _phase;
        t = t * t * (3.0 - 2.0 * t); // smoothstep for click-free interpolation
        return _prev + (_target - _prev) * t;
    }
}
