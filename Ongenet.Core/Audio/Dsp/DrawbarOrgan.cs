using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A single-voice tonewheel-organ oscillator: nine sine partials at the classic Hammond drawbar ratios
/// (16′, 5⅓′, 8′, 4′, 2⅔′, 2′, 1⅗′, 1⅓′, 1′), each with its own 0..1 level. Being pure additive sine
/// synthesis it is naturally band-limited (no aliasing), and an optional shared vibrato gently detunes
/// the whole voice for the chorus/vibrato "scanner" wobble. All trig is done per sample from one phase
/// accumulator; <see cref="Process"/> is allocation-free. Hold one per voice. Reusable.
/// </summary>
public sealed class DrawbarOrgan
{
    public const int DrawbarCount = 9;

    // Harmonic multipliers of the 8′ fundamental for each drawbar footage.
    private static readonly double[] Ratios = { 0.5, 1.5, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 8.0 };

    private readonly float[] _levels = new float[DrawbarCount];
    private readonly Lfo _vibrato = new();

    private double _phase;      // fundamental phase in cycles [0,1)
    private double _inc;        // cycles per sample
    private double _sampleRate = 44100.0;
    private double _vibratoDepth;  // in cents, applied as a phase-increment scale
    private bool _vibratoOn;

    public DrawbarOrgan()
    {
        // A pleasant default registration (mellow "88 8000 000"-ish).
        _levels[0] = 0.8f; _levels[1] = 0.8f; _levels[2] = 1.0f;
        _vibrato.Wave = LfoWave.Sine;
    }

    public void Configure(double baseFreqHz, int sampleRate)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 44100.0;
        _inc = Math.Max(0.0, baseFreqHz) / _sampleRate;
    }

    /// <summary>Sets a drawbar level in [0, 1].</summary>
    public void SetDrawbar(int index, double level)
    {
        if ((uint)index >= DrawbarCount) return;
        _levels[index] = AudioMath.Clamp((float)level, 0f, 1f);
    }

    /// <summary>Configures the shared vibrato. Pass <paramref name="depthCents"/> ≤ 0 to disable.</summary>
    public void SetVibrato(double rateHz, double depthCents)
    {
        _vibratoDepth = Math.Max(0.0, depthCents);
        _vibratoOn = _vibratoDepth > 0.0;
        _vibrato.SetRate(Math.Max(0.0, rateHz), (int)_sampleRate);
    }

    public void Reset(double phase = 0.0)
    {
        _phase = phase - Math.Floor(phase);
        _vibrato.Reset();
    }

    /// <summary>Produces the next voice sample in ~[-1, 1] and advances the phase.</summary>
    public float Process()
    {
        var inc = _inc;
        if (_vibratoOn)
        {
            // Convert cents to a frequency ratio: 2^(cents/1200).
            var cents = _vibratoDepth * _vibrato.Next();
            inc *= Math.Pow(2.0, cents / 1200.0);
        }

        double sum = 0.0;
        double norm = 0.0;
        for (var d = 0; d < DrawbarCount; d++)
        {
            var lvl = _levels[d];
            if (lvl <= 0f) continue;
            sum += lvl * Math.Sin(2.0 * Math.PI * _phase * Ratios[d]);
            norm += lvl;
        }

        _phase += inc;
        if (_phase >= 1.0) _phase -= 1.0;

        return norm > 1e-6 ? (float)(sum / norm) : 0f;
    }
}
