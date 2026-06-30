using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A single-channel circular delay buffer. Per frame, read relative to the next write position
/// (<see cref="ReadInt"/>/<see cref="ReadFrac"/>) then <see cref="Write"/> — matching the
/// read-before-write convention used by the delay/modulation effects. Effects hold one per channel.
/// </summary>
public sealed class DelayLine
{
    private float[] _buf = Array.Empty<float>();
    private int _size;
    private int _write;

    public int Size => _size;

    public void Resize(int size)
    {
        _size = Math.Max(1, size);
        _buf = new float[_size];
        _write = 0;
    }

    public void Clear()
    {
        Array.Clear(_buf, 0, _buf.Length);
        _write = 0;
    }

    /// <summary>Reads <paramref name="delaySamples"/> samples back (integer).</summary>
    public float ReadInt(int delaySamples)
    {
        var i = _write - delaySamples;
        if (i < 0) i += _size;
        return _buf[i];
    }

    /// <summary>Reads a fractional delay back with linear interpolation.</summary>
    public float ReadFrac(double delaySamples)
    {
        if (_size <= 0) return 0f;
        // Wrap the read position into [0, _size) robustly — handles large or even negative delays without
        // spinning or indexing out of bounds (a torn delay/size must never crash the audio thread).
        var rp = _write - delaySamples;
        rp -= Math.Floor(rp / _size) * _size;
        var i0 = (int)rp;
        if (i0 < 0) i0 = 0; else if (i0 >= _size) i0 = _size - 1;
        var frac = (float)(rp - i0);
        var i1 = i0 + 1;
        if (i1 >= _size) i1 -= _size;
        return _buf[i0] * (1 - frac) + _buf[i1] * frac;
    }

    /// <summary>Writes the next sample and advances the write position.</summary>
    public void Write(float x)
    {
        _buf[_write] = x;
        if (++_write >= _size) _write = 0;
    }
}
