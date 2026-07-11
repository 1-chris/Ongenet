using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>Hann analysis/synthesis window (Rubber Band <c>Window&lt;float&gt;(HannWindow)</c>).</summary>
internal sealed class RubberBandWindow
{
    private readonly float[] _coefficients;
    private readonly float _area;

    public RubberBandWindow(int size)
    {
        _coefficients = new float[size];
        if (size <= 1)
        {
            _coefficients[0] = 1f;
            _area = 1f;
            return;
        }

        double sum = 0;
        for (var i = 0; i < size; i++)
        {
            var w = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / size));
            _coefficients[i] = w;
            sum += w;
        }

        _area = (float)(sum / size);
    }

    public int Size => _coefficients.Length;
    public float Area => _area;

    public void Cut(Span<float> block)
    {
        for (var i = 0; i < block.Length && i < _coefficients.Length; i++)
            block[i] *= _coefficients[i];
    }

    public void Cut(ReadOnlySpan<float> src, Span<float> dst)
    {
        var n = Math.Min(src.Length, Math.Min(dst.Length, _coefficients.Length));
        for (var i = 0; i < n; i++)
            dst[i] = src[i] * _coefficients[i];
    }

    public void AddToAccumulator(Span<float> accumulator, float scale)
    {
        var n = Math.Min(accumulator.Length, _coefficients.Length);
        for (var i = 0; i < n; i++)
            accumulator[i] += _coefficients[i] * scale;
    }

    public float this[int i] => _coefficients[i];
}
