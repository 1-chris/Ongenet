using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// The iterated "EQ-boost → distort → clean-up" chain that gives aggressive (hardstyle) sounds their
/// dense, screaming harmonic content. Each of N serial stages peak-boosts a swept centre frequency to
/// feed the waveshaper, distorts it (with asymmetry for even harmonics), then DC-blocks and tames the
/// result before the next stage — so stacking stages adds harmonic generations <b>without</b> runaway
/// level (the per-stage waveshaper normalises to ~unity, plus a small fixed trim). Mono, per-voice
/// state; coefficients are precomputed in <see cref="Configure"/> so the audio loop is allocation- and
/// trig-free. Reusable by any instrument/effect that wants a thick multi-stage distortion.
/// </summary>
public sealed class DistortionStack
{
    public const int MaxStages = 20;

    private readonly BiquadCoefficients[] _coeffs = new BiquadCoefficients[MaxStages];
    private readonly Biquad[] _bq = new Biquad[MaxStages];
    private readonly OnePole[] _dc = new OnePole[MaxStages];   // per-stage DC block (kills asymmetry DC)
    private readonly OnePole[] _tone = new OnePole[MaxStages]; // per-stage "tame the fizz" low-pass

    private int _stages;
    private float _driveLin = 1f;
    private float _bias;
    private float _trim = 1f;
    private ShaperType _type = ShaperType.Tanh;

    public DistortionStack()
    {
        for (var i = 0; i < MaxStages; i++)
        {
            _dc[i] = new OnePole();
            _tone[i] = new OnePole();
        }
    }

    /// <summary>
    /// Lays out the stages for a hit. <paramref name="screamHz"/> is stage 0's boost centre;
    /// <paramref name="spread"/> scales how far the centre sweeps (log, up to ~2.5 octaves) across the
    /// stages, distributing the "scream" across the spectrum. Call once per note (cheap-ish), then
    /// <see cref="Process"/> per sample.
    /// </summary>
    public void Configure(int stages, double screamHz, double spread, double boostDb, double q,
        double driveDb, double asym, double toneHz, ShaperType type, int sampleRate)
    {
        _stages = Math.Clamp(stages, 0, MaxStages);
        _type = type;
        _driveLin = (float)AudioMath.Db2Lin(driveDb);
        _bias = (float)(Math.Clamp(asym, 0.0, 1.0) * 0.5);
        _trim = (float)AudioMath.Db2Lin(-2.0); // gentle per-stage cut so in-band level can't accumulate
        q = Math.Clamp(q, 0.3, 1.8);
        var tone = AudioMath.Clamp(toneHz, 500.0, sampleRate * 0.45);

        for (var i = 0; i < _stages; i++)
        {
            var frac = _stages <= 1 ? 0.0 : (double)i / (_stages - 1);
            var fc = AudioMath.Clamp(screamHz * Math.Pow(2.0, spread * frac * 2.5), 120.0, 9000.0);
            _coeffs[i] = BiquadCoefficients.ComputeEq(EqBandType.Bell, fc, q, boostDb, sampleRate);
            _tone[i].SetLowpass(tone, sampleRate);
            _dc[i].SetLowpass(18.0, sampleRate); // ProcessHP() = x − LP(x), so this is an ~18 Hz DC block
        }
    }

    public void Reset()
    {
        for (var i = 0; i < MaxStages; i++)
        {
            _bq[i].Reset();
            _dc[i].Reset();
            _tone[i].Reset();
        }
    }

    /// <summary>Runs one sample through all configured stages. With 0 stages this is a pass-through.</summary>
    public float Process(float x)
    {
        for (var i = 0; i < _stages; i++)
        {
            var boosted = _bq[i].Process(_coeffs[i], x);                       // emphasise the scream band
            var shaped = WaveShaper.Shape((float)boosted, _type, _driveLin, _bias); // ~unity harmonics
            var blocked = _dc[i].ProcessHP(shaped);                            // remove asymmetry DC
            var toned = _tone[i].ProcessLP(blocked);                           // tame harsh highs
            x = (float)(toned * _trim);
        }

        return x;
    }
}
