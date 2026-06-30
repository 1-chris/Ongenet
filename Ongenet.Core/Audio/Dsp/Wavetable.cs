using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// An immutable wavetable: a stack of equal-length single-cycle frames. Reading interpolates both across
/// frames (the scan/morph position) and within a frame (the oscillator phase). To stay alias-free as the
/// pitch rises, the table is mip-mapped at construction: each mip is a band-limited, half-length copy of
/// the previous one (top octave of harmonics removed), and the band-limited read picks the finest mip whose
/// harmonics all fit below Nyquist for the requested playback rate.
///
/// Immutable so it can be shared lock-free between the audio thread (voices read it) and the UI (the 3D
/// view draws it) — a new table is published by reference on rebuild. Reusable.
/// </summary>
public sealed class Wavetable
{
    private readonly float[][] _mips; // [mip] -> FrameCount * MipLength[mip], frame-major
    private readonly int[] _mipLen;

    public Wavetable(float[] data, int frameCount, int frameLength)
    {
        if (frameCount < 1) frameCount = 1;
        if (frameLength < 2) frameLength = 2;
        FrameCount = frameCount;
        FrameLength = frameLength;

        var mip0 = data.Length >= frameCount * frameLength ? data : new float[frameCount * frameLength];
        (_mips, _mipLen) = BuildMips(mip0, frameCount, frameLength);
    }

    public int FrameCount { get; }
    public int FrameLength { get; }

    /// <summary>Reads a full-resolution sample by frame + index (for visualization/analysis). Bounds-safe.</summary>
    public float Sample(int frame, int index)
    {
        if (frame < 0) frame = 0; else if (frame >= FrameCount) frame = FrameCount - 1;
        if (index < 0) index = 0; else if (index >= FrameLength) index = FrameLength - 1;
        return _mips[0][frame * FrameLength + index];
    }

    /// <summary>Full-resolution morph read (no band-limiting) — for display/analysis.</summary>
    public float Read(float position, float phase) => ReadFrom(_mips[0], FrameLength, position, phase);

    /// <summary>
    /// Band-limited morph read for audio. <paramref name="inc"/> is the oscillator phase increment per
    /// sample (cycles/sample = f0/sampleRate); the finest alias-free mip is chosen from it.
    /// </summary>
    public float Read(float position, float phase, float inc)
    {
        var maxHarmonic = inc > 1e-7f ? 0.5f / inc : float.MaxValue; // harmonics that stay below Nyquist
        var m = 0;
        while (m < _mips.Length - 1 && (_mipLen[m] >> 1) > maxHarmonic) m++;
        return ReadFrom(_mips[m], _mipLen[m], position, phase);
    }

    private float ReadFrom(float[] data, int len, float position, float phase)
    {
        if (position < 0f) position = 0f; else if (position > 1f) position = 1f;
        phase -= MathF.Floor(phase);

        var fc = position * (FrameCount - 1);
        var f0 = (int)fc;
        var ff = fc - f0;
        var f1 = f0 + 1 < FrameCount ? f0 + 1 : f0;

        var p = phase * len;
        var i0 = (int)p;
        var pf = p - i0;
        var i1 = i0 + 1 < len ? i0 + 1 : 0;

        var baseA = f0 * len;
        var baseB = f1 * len;
        var a = data[baseA + i0] + (data[baseA + i1] - data[baseA + i0]) * pf;
        var b = data[baseB + i0] + (data[baseB + i1] - data[baseB + i0]) * pf;
        return a + (b - a) * ff;
    }

    // Builds the mip pyramid: mip 0 is the full table; each further mip halves the length with the top
    // octave of harmonics removed (via FFT truncation), so high notes can read a mip with no aliasing.
    private static (float[][] mips, int[] lens) BuildMips(float[] mip0, int frameCount, int frameLength)
    {
        const int minLen = 4; // finest mip keeps ~2 harmonics, enough to stay alias-free at the very top
        if (!Fft.IsPowerOfTwo(frameLength) || frameLength <= minLen)
            return (new[] { mip0 }, new[] { frameLength });

        var levels = 1;
        while ((frameLength >> levels) >= minLen) levels++;

        var mips = new float[levels][];
        var lens = new int[levels];
        mips[0] = mip0;
        lens[0] = frameLength;
        for (var m = 1; m < levels; m++)
        {
            lens[m] = frameLength >> m;
            mips[m] = new float[frameCount * lens[m]];
        }

        var re = new double[frameLength];
        var im = new double[frameLength];
        for (var f = 0; f < frameCount; f++)
        {
            for (var i = 0; i < frameLength; i++) { re[i] = mip0[f * frameLength + i]; im[i] = 0; }
            Fft.Forward(re, im);

            for (var m = 1; m < levels; m++)
            {
                var len = lens[m];
                var half = len / 2;
                var sr = new double[len];
                var si = new double[len];
                // Keep harmonics 0..half from the full spectrum (Hermitian-symmetric), drop the rest.
                for (var k = 0; k <= half; k++) { sr[k] = re[k]; si[k] = im[k]; }
                for (var k = 1; k < half; k++) { sr[len - k] = re[k]; si[len - k] = -im[k]; }
                Fft.Inverse(sr, si);

                var dst = f * len;
                for (var i = 0; i < len; i++) mips[m][dst + i] = (float)sr[i];
                NormalizeFrame(mips[m], dst, len);
            }
        }

        return (mips, lens);
    }

    private static void NormalizeFrame(float[] data, int offset, int length)
    {
        var peak = 0f;
        for (var i = 0; i < length; i++)
        {
            var a = MathF.Abs(data[offset + i]);
            if (a > peak) peak = a;
        }

        if (peak < 1e-6f) return;
        var gain = 0.9f / peak;
        for (var i = 0; i < length; i++) data[offset + i] *= gain;
    }
}
