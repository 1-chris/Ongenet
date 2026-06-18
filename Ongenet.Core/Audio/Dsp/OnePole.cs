using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A one-pole filter for parameter smoothing and gentle tone shaping:
/// <c>y = (1−a)·x + a·y</c>. Configure as a low-pass by cutoff or by a smoothing time constant.
/// </summary>
public sealed class OnePole
{
    private double _z;
    private double _a;

    public void SetLowpass(double hz, double sampleRate)
        => _a = sampleRate > 0 ? Math.Exp(-2.0 * Math.PI * Math.Max(0.0, hz) / sampleRate) : 0;

    public void SetSmoothTime(double ms, double sampleRate)
        => _a = Math.Exp(-1.0 / (Math.Max(0.0001, ms / 1000.0) * sampleRate));

    public void Reset(double value = 0) => _z = value;

    public double ProcessLP(double x)
    {
        _z = (1.0 - _a) * x + _a * _z;
        return _z;
    }

    public double ProcessHP(double x) => x - ProcessLP(x);
}
