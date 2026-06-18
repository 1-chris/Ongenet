using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// One band of the <see cref="EqEffect"/>: a biquad with a type, centre/cutoff frequency, gain
/// (for Bell/shelves) and Q. Holds its own per-channel filter state and caches coefficients,
/// recomputing only when a control changes.
/// </summary>
public sealed class EqBand
{
    public EqBandType Type { get; set; } = EqBandType.Bell;
    public double Frequency { get; set; } = 1000.0;
    public double GainDb { get; set; }
    public double Q { get; set; } = 1.0;

    private Biquad[] _bq = new Biquad[2];

    private BiquadCoefficients _coeffs = BiquadCoefficients.Identity;
    private double _lastFreq = double.NaN, _lastGain = double.NaN, _lastQ = double.NaN, _lastSr = double.NaN;
    private EqBandType _lastType = (EqBandType)(-1);

    public EqBand() { }

    public EqBand(EqBandType type, double frequency, double gainDb, double q)
    {
        Type = type; Frequency = frequency; GainDb = gainDb; Q = q;
    }

    public EqBand Clone() => new(Type, Frequency, GainDb, Q);

    /// <summary>Allocates per-channel state and forces a coefficient recompute.</summary>
    public void Prepare(int channels)
    {
        if (channels < 1) channels = 1;
        _bq = new Biquad[channels];
        _lastType = (EqBandType)(-1);
    }

    /// <summary>Recomputes coefficients if any controlling value changed.</summary>
    public void EnsureCoeffs(double sampleRate)
    {
        if (Type == _lastType && Frequency == _lastFreq && GainDb == _lastGain && Q == _lastQ && sampleRate == _lastSr)
            return;
        _coeffs = BiquadCoefficients.ComputeEq(Type, Frequency, Q, GainDb, sampleRate);
        _lastType = Type; _lastFreq = Frequency; _lastGain = GainDb; _lastQ = Q; _lastSr = sampleRate;
    }

    public float Process(int channel, float input)
    {
        if (channel >= _bq.Length) return input;
        return (float)_bq[channel].Process(_coeffs, input);
    }
}
