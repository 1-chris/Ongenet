using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A lock-free mono ring buffer for the analyser displays: the audio thread taps a downmix of a
/// processed block (<see cref="Tap"/>); the UI copies the most recent window out (<see cref="CaptureLatest"/>).
/// Backs <c>ISpectrumSource</c> implementations (Filter/EQ), removing their duplicated capture code.
/// </summary>
public sealed class SpectrumScope
{
    private const int Size = 4096; // power of two
    private readonly float[] _scope = new float[Size];
    private int _write;

    /// <summary>Appends a mono downmix of an interleaved block to the ring (audio thread).</summary>
    public void Tap(ReadOnlySpan<float> buffer, int channels)
    {
        if (channels < 1) channels = 1;
        var frames = buffer.Length / channels;
        var w = _write;
        for (var n = 0; n < frames; n++)
        {
            float sum = 0;
            var i = n * channels;
            for (var c = 0; c < channels; c++) sum += buffer[i + c];
            _scope[w] = sum / channels;
            w = (w + 1) & (Size - 1);
        }

        _write = w;
    }

    /// <summary>Copies the most recent dest.Length samples (chronological order). Returns the count written.</summary>
    public int CaptureLatest(float[] dest)
    {
        var n = Math.Min(dest.Length, Size);
        var start = (_write - n) & (Size - 1);
        for (var i = 0; i < n; i++) dest[i] = _scope[(start + i) & (Size - 1)];
        return n;
    }
}
