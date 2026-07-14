using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A multi-band gain shaper built on <see cref="FilterBank"/>: it splits the signal into N log-spaced
/// band-pass bands, scales each by an independent linear gain, and sums them back. Because the bands are
/// constant-0 dB band-passes that overlap smoothly, unity gains reconstruct (near-)transparently while
/// per-band boosts/cuts carve a graphic-EQ / spectral-morph curve — the "sculpt" control surface. The
/// band scratch buffer is allocated in <see cref="Configure"/>, so <see cref="Process"/> is
/// allocation-free. Hold one per channel. Reusable by any spectral/EQ effect.
/// </summary>
public sealed class HarmonicSculptor
{
    private readonly FilterBank _bank = new();
    private float[] _gains = Array.Empty<float>();
    private float[] _scratch = Array.Empty<float>();

    /// <summary>Number of bands.</summary>
    public int BandCount => _gains.Length;

    /// <summary>Centre frequencies (Hz), ascending.</summary>
    public ReadOnlySpan<double> Centers => _bank.Centers;

    public void Configure(int bands, int sampleRate, double minHz = 40.0, double maxHz = 18000.0)
    {
        bands = Math.Max(1, bands);
        var sr = sampleRate > 0 ? sampleRate : 44100;
        maxHz = Math.Min(maxHz, sr * 0.45);

        _bank.Configure(bands, minHz, maxHz, sr);
        var count = _bank.BandCount;

        if (_gains.Length != count)
        {
            _gains = new float[count];
            _scratch = new float[count];
        }

        for (var i = 0; i < count; i++) _gains[i] = 1f; // start flat
    }

    /// <summary>Sets a band's linear gain (1 = unity, 0 = muted, &gt;1 = boost).</summary>
    public void SetBandGain(int index, double gainLin)
    {
        if ((uint)index >= (uint)_gains.Length) return;
        _gains[index] = (float)Math.Max(0.0, gainLin);
    }

    public void Reset() => _bank.Reset();

    /// <summary>Filters one sample into the bands and sums them by their gains.</summary>
    public float Process(float sample)
    {
        var n = _gains.Length;
        if (n == 0) return sample;

        _bank.Process(sample, _scratch);

        var sum = 0f;
        for (var b = 0; b < n; b++) sum += _scratch[b] * _gains[b];
        return sum;
    }
}
