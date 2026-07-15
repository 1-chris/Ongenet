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
        var next = Math.Max(1, size);
        var buf = new float[next];
        // Publish the buffer first so torn reads of Size/_buf never outrun the live array length.
        _buf = buf;
        _size = next;
        _write = 0;
    }

    public void Clear()
    {
        var buf = _buf;
        Array.Clear(buf, 0, buf.Length);
        _write = 0;
    }

    /// <summary>Reads <paramref name="delaySamples"/> samples back (integer).</summary>
    public float ReadInt(int delaySamples)
    {
        var buf = _buf;
        var size = buf.Length;
        if (size <= 0) return 0f;
        // Index using the live buffer length — Prepare/Resize may run on another thread and briefly
        // leave _size and _buf out of sync; a torn read must never crash the audio thread.
        delaySamples = Math.Clamp(delaySamples, 0, size - 1);
        var i = _write - delaySamples;
        i %= size;
        if (i < 0) i += size;
        return buf[i];
    }

    /// <summary>Reads a fractional delay back with linear interpolation.</summary>
    public float ReadFrac(double delaySamples)
    {
        var buf = _buf;
        var size = buf.Length;
        if (size <= 0) return 0f;
        // Wrap the read position into [0, size) robustly — handles large or even negative delays without
        // spinning or indexing out of bounds (a torn delay/size must never crash the audio thread).
        var rp = _write - delaySamples;
        rp -= Math.Floor(rp / size) * size;
        var i0 = (int)rp;
        if (i0 < 0) i0 = 0; else if (i0 >= size) i0 = size - 1;
        var frac = (float)(rp - i0);
        var i1 = i0 + 1;
        if (i1 >= size) i1 -= size;
        return buf[i0] * (1 - frac) + buf[i1] * frac;
    }

    /// <summary>Writes the next sample and advances the write position.</summary>
    public void Write(float x)
    {
        var buf = _buf;
        var size = buf.Length;
        if (size <= 0) return;
        var w = _write;
        if (w >= size) w = 0;
        buf[w] = x;
        _write = w + 1 >= size ? 0 : w + 1;
    }
}
