using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Additive partial bank for resynth and morphing timbres. Powers wavetable resynth mode and Field nodes.
/// </summary>
public sealed class AdditivePartialEngine
{
    public const int MaxPartials = 64;

    private readonly double[] _freq = new double[MaxPartials];
    private readonly double[] _amp = new double[MaxPartials];
    private readonly double[] _phase = new double[MaxPartials];
    private int _partialCount = 16;
    private double _sampleRate = 44100.0;
    private double _fundamental = 440.0;

    public int PartialCount
    {
        get => _partialCount;
        set => _partialCount = Math.Clamp(value, 1, MaxPartials);
    }

    public void SetSampleRate(double sampleRate) => _sampleRate = sampleRate > 0 ? sampleRate : 44100.0;

    public void SetFundamental(double hz)
    {
        _fundamental = Math.Max(hz, 20.0);
        for (var i = 0; i < _partialCount; i++)
            _freq[i] = _fundamental * Math.Max((i + 1), 0.5);
    }

    public void SetPartial(int index, double harmonic, double amplitude)
    {
        if ((uint)index >= MaxPartials) return;
        _freq[index] = _fundamental * Math.Max(harmonic, 0.5);
        _amp[index] = Math.Clamp(amplitude, 0, 1);
    }

    /// <summary>Import magnitudes from a magnitude spectrum (harmonic resynth).</summary>
    public void ImportSpectrum(ReadOnlySpan<float> magnitudes, int binCount)
    {
        var count = Math.Min(_partialCount, MaxPartials);
        binCount = Math.Clamp(binCount, 1, magnitudes.Length);
        for (var i = 0; i < count; i++)
        {
            var harmonic = i + 1;
            var bin = Math.Clamp(harmonic, 0, magnitudes.Length - 1);
            SetPartial(i, harmonic, magnitudes[bin]);
        }
    }

    public float Process()
    {
        var sum = 0.0;
        var count = _partialCount;
        var incScale = Math.PI * 2.0 / _sampleRate;
        for (var i = 0; i < count; i++)
        {
            var inc = _freq[i] * incScale;
            _phase[i] += inc;
            if (_phase[i] > Math.PI * 2) _phase[i] -= Math.PI * 2;
            sum += Math.Sin(_phase[i]) * _amp[i];
        }
        return (float)(sum / Math.Max(count, 1));
    }

    public void ResetPhases() => Array.Clear(_phase);
}
