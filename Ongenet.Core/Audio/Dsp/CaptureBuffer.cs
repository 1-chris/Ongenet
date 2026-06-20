using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A single-channel circular capture buffer addressed by an absolute, ever-increasing sample index.
/// Unlike <see cref="DelayLine"/> (which reads relative to the write head), absolute addressing lets a
/// caller replay a fixed captured region while new input keeps streaming in — exactly what a stutter
/// "buffer" engine needs. Reads interpolate linearly. Capacity is rounded up to a power of two for
/// fast masking. Reusable by any glitch/granular/looping effect.
/// </summary>
public sealed class CaptureBuffer
{
    private float[] _buf = Array.Empty<float>();
    private int _mask;
    private long _write;

    /// <summary>The absolute index that the next <see cref="Write"/> will occupy (= samples written).</summary>
    public long WritePos => _write;

    public int Capacity => _buf.Length;

    public void Resize(int minSize)
    {
        var n = 1;
        while (n < Math.Max(2, minSize)) n <<= 1;
        _buf = new float[n];
        _mask = n - 1;
        _write = 0;
    }

    public void Clear()
    {
        Array.Clear(_buf, 0, _buf.Length);
        _write = 0;
    }

    public void Write(float x)
    {
        _buf[(int)(_write & _mask)] = x;
        _write++;
    }

    /// <summary>Reads (linearly interpolated) at absolute sample position <paramref name="pos"/>.</summary>
    public float ReadAbs(double pos)
    {
        var i0 = (long)Math.Floor(pos);
        var frac = (float)(pos - i0);
        var a = _buf[(int)(i0 & _mask)];
        var b = _buf[(int)((i0 + 1) & _mask)];
        return a * (1f - frac) + b * frac;
    }

    /// <summary>Copies the most recent <paramref name="count"/> samples (ending at the write head) into
    /// <paramref name="dest"/>; positions before the start of history read as 0.</summary>
    public void Snapshot(Span<float> dest, int count)
    {
        var start = _write - count;
        for (var i = 0; i < count; i++)
        {
            var p = start + i;
            dest[i] = p < 0 ? 0f : _buf[(int)(p & _mask)];
        }
    }
}
