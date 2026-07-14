using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A single-sideband (SSB) frequency shifter — it slides the whole spectrum up or down by a fixed number
/// of Hz, breaking the harmonic ratios (unlike a pitch shifter), for metallic, dissonant, "barber-pole"
/// textures. It builds an analytic signal with a wide-band Hilbert network (two parallel cascades of
/// second-order all-pass sections whose outputs stay ~90° apart across the audio band), then heterodynes
/// it with a quadrature oscillator: <c>y = I·cos − Q·sin</c> shifts up, the conjugate shifts down. Mono,
/// allocation-free in <see cref="Process"/>; the shift amount can be swept continuously. Reusable.
/// </summary>
public sealed class FreqShifter
{
    // Squared pole coefficients for a two-path IIR Hilbert (analytic) network. The two cascades, with the
    // "b" path fed one sample late, differ by ~90° from ~20 Hz to ~20 kHz (a widely used minimum-error set).
    private static readonly double[] CoeffsA = { 0.6923877778065106, 0.9360654322959, 0.9882295226860, 0.9987488452737 };
    private static readonly double[] CoeffsB = { 0.4021921162426, 0.8561710882420, 0.9722909545651, 0.9952884791278 };

    private readonly Allpass2[] _pathA = new Allpass2[CoeffsA.Length];
    private readonly Allpass2[] _pathB = new Allpass2[CoeffsB.Length];

    private float _prevIn;   // one-sample delay feeding path B
    private double _phase;   // quadrature oscillator phase [0,1)
    private double _inc;     // shift in cycles per sample (signed)

    public FreqShifter()
    {
        for (var i = 0; i < CoeffsA.Length; i++) _pathA[i] = new Allpass2(CoeffsA[i]);
        for (var i = 0; i < CoeffsB.Length; i++) _pathB[i] = new Allpass2(CoeffsB[i]);
    }

    /// <summary>Sets the shift in Hz (positive = up, negative = down).</summary>
    public void Configure(double shiftHz, int sampleRate)
        => _inc = sampleRate > 0 ? shiftHz / sampleRate : 0.0;

    public void Reset()
    {
        for (var i = 0; i < _pathA.Length; i++) _pathA[i].Reset();
        for (var i = 0; i < _pathB.Length; i++) _pathB[i].Reset();
        _prevIn = 0f;
        _phase = 0;
    }

    /// <summary>Frequency-shifts one sample.</summary>
    public float Process(float sample)
    {
        // In-phase branch reads the sample now; quadrature branch reads it one sample late — the extra
        // z^-1 is what makes the two all-pass cascades come out a quarter-cycle apart.
        double i = sample;
        for (var s = 0; s < _pathA.Length; s++) i = _pathA[s].Process(i);

        double q = _prevIn;
        for (var s = 0; s < _pathB.Length; s++) q = _pathB[s].Process(q);
        _prevIn = sample;

        var cos = Math.Cos(_phase * 2.0 * Math.PI);
        var sin = Math.Sin(_phase * 2.0 * Math.PI);
        _phase += _inc;
        if (_phase >= 1.0) _phase -= 1.0;
        else if (_phase < 0.0) _phase += 1.0;

        return (float)(i * cos - q * sin);
    }

    /// <summary>
    /// A second-order all-pass section (<c>y[n] = a·x[n] + x[n-2] − a·y[n-2]</c>) — the building block of
    /// the phase-difference (Hilbert) network. Mutable struct so an array gives cheap per-section state.
    /// </summary>
    private struct Allpass2
    {
        private readonly double _a;
        private double _x1, _x2, _y1, _y2;

        public Allpass2(double a)
        {
            _a = a;
            _x1 = _x2 = _y1 = _y2 = 0;
        }

        public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;

        public double Process(double x)
        {
            var y = _a * (x + _y2) - _x2;
            _x2 = _x1; _x1 = x;
            _y2 = _y1; _y1 = y;
            return y;
        }
    }
}
