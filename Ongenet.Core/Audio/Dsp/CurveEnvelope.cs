using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A one-shot envelope expressed as a <b>pure function of absolute time</b>: delay → (linear) attack →
/// hold → curved decay → 0. Being stateless (no per-sample machine, no gate) it can be evaluated at any
/// time both on the audio thread and offline — so a real-time voice and a rendered preview stay
/// identical. The decay is a normalised exponential that stays punchy yet lands <i>exactly</i> at 0 at
/// the end of the decay, which lets a percussion voice compute a finite, deterministic total length.
/// Reusable for amp, pitch sweeps, transients and tails.
/// </summary>
public readonly struct CurveEnvelope
{
    private readonly double _delay;
    private readonly double _attack;
    private readonly double _hold;
    private readonly double _decay;
    private readonly double _k;        // decay steepness derived from curve
    private readonly double _expNegK;  // cached e^-k
    private readonly double _norm;     // 1 / (1 - e^-k)

    /// <param name="delaySec">Silence before the envelope starts.</param>
    /// <param name="attackSec">Linear rise 0→1 (keep short for percussion; ~0.5 ms declicks).</param>
    /// <param name="holdSec">Time held at 1 before the decay.</param>
    /// <param name="decaySec">Curved fall 1→0.</param>
    /// <param name="curve">0 = nearly linear decay, 1 = very snappy/exponential.</param>
    public CurveEnvelope(double delaySec, double attackSec, double holdSec, double decaySec, double curve)
    {
        _delay = Math.Max(0.0, delaySec);
        _attack = Math.Max(0.0, attackSec);
        _hold = Math.Max(0.0, holdSec);
        _decay = Math.Max(0.0, decaySec);
        _k = 3.0 + Math.Clamp(curve, 0.0, 1.0) * 7.0;
        _expNegK = Math.Exp(-_k);
        _norm = 1.0 / (1.0 - _expNegK);
    }

    /// <summary>The time (s) at which the envelope first reaches 0 and stays there.</summary>
    public double TotalSeconds => _delay + _attack + _hold + _decay;

    /// <summary>Evaluates the envelope at elapsed time <paramref name="tSec"/>, returning [0, 1].</summary>
    public double Evaluate(double tSec)
    {
        var t = tSec - _delay;
        if (t <= 0.0) return 0.0;

        if (t < _attack) return _attack > 0 ? t / _attack : 1.0;
        t -= _attack;

        if (t < _hold) return 1.0;
        t -= _hold;

        if (_decay <= 0.0 || t >= _decay) return 0.0;

        var u = t / _decay; // 0..1 across the decay
        // Normalised exponential: 1 at u=0, exactly 0 at u=1.
        return (Math.Exp(-_k * u) - _expNegK) * _norm;
    }
}
