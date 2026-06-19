using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A bank of log-spaced band-pass filters that splits one channel's signal into N frequency bands.
/// Built from the RBJ <see cref="BiquadCoefficients"/> (constant 0 dB band-pass) with a Q derived from
/// the per-band octave spacing, so adjacent bands overlap smoothly. Hold one instance per channel.
/// Reusable by any multiband/spectral effect (vocoder, multiband dynamics, analysers).
/// </summary>
public sealed class FilterBank
{
    private BiquadCoefficients[] _coeffs = Array.Empty<BiquadCoefficients>();
    private Biquad[] _state = Array.Empty<Biquad>();
    private double[] _centers = Array.Empty<double>();

    /// <summary>Number of bands.</summary>
    public int BandCount => _coeffs.Length;

    /// <summary>Centre frequencies (Hz), ascending.</summary>
    public ReadOnlySpan<double> Centers => _centers;

    /// <summary>
    /// Builds <paramref name="bands"/> log-spaced band-pass filters spanning
    /// <paramref name="minHz"/>..<paramref name="maxHz"/> at <paramref name="sampleRate"/>.
    /// </summary>
    public void Configure(int bands, double minHz, double maxHz, double sampleRate)
    {
        bands = Math.Max(1, bands);
        minHz = Math.Max(20.0, minHz);
        maxHz = Math.Max(minHz * 2.0, maxHz);

        _coeffs = new BiquadCoefficients[bands];
        _state = new Biquad[bands];
        _centers = new double[bands];

        // Octaves between adjacent band centres → a Q that gives roughly that bandwidth.
        var octavesPerBand = bands > 1 ? Math.Log(maxHz / minHz, 2.0) / (bands - 1) : 1.0;
        var q = QForBandwidth(Math.Max(0.05, octavesPerBand));
        var ratio = bands > 1 ? Math.Pow(maxHz / minHz, 1.0 / (bands - 1)) : 1.0;

        var center = minHz;
        for (var b = 0; b < bands; b++)
        {
            _centers[b] = center;
            _coeffs[b] = BiquadCoefficients.Compute(FilterMode.BandPass, center, q, sampleRate);
            _state[b].Reset();
            center *= ratio;
        }
    }

    public void Reset()
    {
        for (var b = 0; b < _state.Length; b++) _state[b].Reset();
    }

    /// <summary>Filters one input sample into each band; writes <see cref="BandCount"/> values into <paramref name="bandsOut"/>.</summary>
    public void Process(float input, Span<float> bandsOut)
    {
        var n = _coeffs.Length;
        for (var b = 0; b < n; b++)
            bandsOut[b] = (float)_state[b].Process(in _coeffs[b], input);
    }

    // Q for a band-pass spanning the given bandwidth in octaves (RBJ cookbook relation).
    private static double QForBandwidth(double octaves)
    {
        var x = octaves * Math.Log(2.0) / 2.0;
        var sinh = Math.Sinh(x);
        return sinh > 1e-9 ? 1.0 / (2.0 * sinh) : 10.0;
    }
}
