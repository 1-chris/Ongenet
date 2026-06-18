using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A peak envelope follower with separate attack/release one-pole smoothing — the classic dynamics
/// detector: <c>env = x + coef·(env − x)</c>, coef = exp(−1 / (timeSeconds · sampleRate)).
/// Feed it a rectified (≥0) detector value.
/// </summary>
public sealed class EnvelopeFollower
{
    private double _env;
    private double _attack = 0.0;
    private double _release = 0.0;

    public double Value => _env;

    public void Reset() => _env = 0;

    public void SetTimes(double attackMs, double releaseMs, double sampleRate)
    {
        _attack = Coef(attackMs, sampleRate);
        _release = Coef(releaseMs, sampleRate);
    }

    public double Process(double rectified)
    {
        var coef = rectified > _env ? _attack : _release;
        _env = rectified + coef * (_env - rectified);
        return _env;
    }

    private static double Coef(double ms, double sampleRate)
    {
        var samples = Math.Max(0.0001, ms / 1000.0) * sampleRate;
        return Math.Exp(-1.0 / samples);
    }
}
