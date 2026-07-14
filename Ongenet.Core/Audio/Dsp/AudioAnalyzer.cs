using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>Level, phase, and stereo correlation analysis for Tool-style utility meters.</summary>
public sealed class AudioAnalyzer
{
    private float _peakL;
    private float _peakR;
    private float _rmsAcc;
    private int _rmsCount;
    private float _corrAcc;
    private int _corrCount;

    public float PeakLeft { get; private set; }
    public float PeakRight { get; private set; }
    public float Rms { get; private set; }
    public float Correlation { get; private set; }
    public float PhaseDegrees { get; private set; }

    public void Reset()
    {
        _peakL = _peakR = 0;
        _rmsAcc = 0;
        _rmsCount = 0;
        _corrAcc = 0;
        _corrCount = 0;
    }

    public void ProcessFrame(float left, float right)
    {
        var al = MathF.Abs(left);
        var ar = MathF.Abs(right);
        if (al > _peakL) _peakL = al;
        if (ar > _peakR) _peakR = ar;
        _rmsAcc += 0.5f * (left * left + right * right);
        _rmsCount++;
        _corrAcc += left * right;
        _corrCount++;
    }

    public void CommitBlock()
    {
        PeakLeft = _peakL;
        PeakRight = _peakR;
        Rms = _rmsCount > 0 ? MathF.Sqrt(_rmsAcc / _rmsCount) : 0;
        if (_corrCount > 0)
        {
            var denom = MathF.Max(1e-6f, PeakLeft * PeakRight);
            Correlation = Math.Clamp(_corrAcc / (_corrCount * denom), -1f, 1f);
            PhaseDegrees = MathF.Acos(Correlation) * (180f / MathF.PI);
        }

        Reset();
    }
}
