using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Multi-bank resonant filter with per-bank envelope gating (Love-Philter style). Shared by filter/ladder upgrades.
/// </summary>
public sealed class MultibankGateFilterDsp
{
    public const int BankCount = 8;

    private readonly Biquad[] _filters = new Biquad[BankCount];
    private readonly DahdsrEnvelope[] _envs = new DahdsrEnvelope[BankCount];
    private double _sampleRate = 44100.0;

    public readonly double[] Cutoffs = new double[BankCount];
    public readonly double[] Resonances = new double[BankCount];
    public readonly double[] Levels = new double[BankCount];

    public MultibankGateFilterDsp()
    {
        for (var i = 0; i < BankCount; i++)
        {
            _filters[i] = new Biquad();
            _envs[i] = new DahdsrEnvelope();
            Cutoffs[i] = 200 * Math.Pow(2, i * 0.6);
            Resonances[i] = 1.2;
            Levels[i] = i == 0 ? 1.0 : 0.0;
        }
    }

    public void Prepare(double sampleRate)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 44100.0;
        foreach (var e in _envs) e.SetSampleRate((int)_sampleRate);
        foreach (var f in _filters) f.Reset();
    }

    public void Gate()
    {
        foreach (var e in _envs)
        {
            e.AttackSeconds = 0.002;
            e.DecaySeconds = 0.12;
            e.SustainLevel = 0.0;
            e.ReleaseSeconds = 0.08;
            e.Gate();
        }
    }

    /// <summary>Opens all bank envelopes and holds at unity — for continuous filter use.</summary>
    public void HoldOpen()
    {
        foreach (var e in _envs)
        {
            e.AttackSeconds = 0.001;
            e.DecaySeconds = 0.001;
            e.SustainLevel = 1.0;
            e.ReleaseSeconds = 1.0;
            e.Gate();
        }
    }

    public void Release()
    {
        foreach (var e in _envs) e.Release();
    }

    public float Process(float input)
    {
        var sum = 0f;
        for (var i = 0; i < BankCount; i++)
        {
            if (Levels[i] <= 1e-6) continue;
            var env = (float)_envs[i].Process();
            var coeffs = BiquadCoefficients.Compute(FilterMode.BandPass, Cutoffs[i], Resonances[i], _sampleRate);
            sum += (float)_filters[i].Process(coeffs, input) * env * (float)Levels[i];
        }
        return sum;
    }

    public bool IsActive
    {
        get
        {
            foreach (var e in _envs)
                if (e.IsActive) return true;
            return false;
        }
    }
}
