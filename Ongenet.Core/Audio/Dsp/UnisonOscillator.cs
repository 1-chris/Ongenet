using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A stereo "supersaw"-style unison stack: several <see cref="WaveOscillator"/>s detuned around a
/// base frequency and spread across the stereo field, summed into one fat, wide tone. The classic
/// building block for huge pads/leads. Allocation-free after construction (a fixed pool sized to
/// <c>maxVoices</c>), so it is safe on the audio thread. Reusable by any instrument.
/// </summary>
/// <remarks>
/// Usage per note: <see cref="SetSampleRate"/> and <see cref="Seed"/> once on start, then
/// <see cref="Configure"/> + <see cref="SetBaseFrequency"/> whenever the voice/detune changes
/// (typically once per control block), and <see cref="Render"/> once per sample.
/// </remarks>
public sealed class UnisonOscillator
{
    private readonly WaveOscillator[] _oscs;
    private readonly double[] _ratios;   // per-voice detune as a frequency multiplier
    private readonly float[] _panL;
    private readonly float[] _panR;
    private readonly float[] _gain;      // per-voice amplitude (centre vs. detuned blend), pre-normalised
    private int _count = 1;

    public UnisonOscillator(int maxVoices)
    {
        if (maxVoices < 1) maxVoices = 1;
        _oscs = new WaveOscillator[maxVoices];
        _ratios = new double[maxVoices];
        _panL = new float[maxVoices];
        _panR = new float[maxVoices];
        _gain = new float[maxVoices];
        for (var i = 0; i < maxVoices; i++)
        {
            _oscs[i] = new WaveOscillator();
            _ratios[i] = 1.0;
            _panL[i] = _panR[i] = 0.70710678f; // centre
            _gain[i] = 1f;
        }
    }

    public int MaxVoices => _oscs.Length;

    /// <summary>Sets the waveform of every unison voice.</summary>
    public OscWave Wave
    {
        set { foreach (var o in _oscs) o.Wave = value; }
    }

    /// <summary>Sets the custom single-cycle wavetable used when <see cref="Wave"/> is <see cref="OscWave.Custom"/>.</summary>
    public float[]? CustomTable
    {
        set { foreach (var o in _oscs) o.CustomTable = value; }
    }

    public void SetSampleRate(int sampleRate)
    {
        foreach (var o in _oscs) o.SetSampleRate(sampleRate);
    }

    /// <summary>
    /// Seeds each voice's noise generator and spreads their start phases, so layered/noise unison
    /// voices decorrelate. Call once per note before <see cref="Render"/>.
    /// </summary>
    public void Seed(uint seed)
    {
        for (var i = 0; i < _oscs.Length; i++)
        {
            _oscs[i].SeedNoise(seed + (uint)i * 0x9E3779B9u);
            // Spread phases evenly across the cycle for a wider, less phasey attack.
            _oscs[i].ResetPhase(_oscs.Length <= 1 ? 0.0 : (double)i / _oscs.Length);
        }
    }

    /// <summary>
    /// Lays out the stack: <paramref name="voices"/> active oscillators detuned by ±<paramref name="detuneCents"/>,
    /// panned out by <paramref name="width"/> (0 = mono, 1 = hard L/R), with detuned voices mixed in at
    /// <paramref name="blend"/> relative to the centre. The summed output is normalised to roughly unity.
    /// </summary>
    public void Configure(int voices, double detuneCents, double width, double blend)
    {
        _count = Math.Clamp(voices, 1, _oscs.Length);
        width = Math.Clamp(width, 0.0, 1.0);
        blend = (float)Math.Clamp(blend, 0.0, 1.0);

        double sumSq = 0;
        for (var i = 0; i < _count; i++)
        {
            // Position in [-1, 1], symmetric about centre.
            var t = _count == 1 ? 0.0 : -1.0 + 2.0 * i / (_count - 1);
            _ratios[i] = MusicalMath.CentsToRatio(t * detuneCents);

            var isCentre = Math.Abs(t) < 1e-6;
            var g = isCentre ? 1f : (float)blend;
            _gain[i] = g;
            sumSq += g * g;

            AudioMath.PanGains(t * width, out var l, out var r);
            _panL[i] = l;
            _panR[i] = r;
        }

        // Normalise so total RMS stays roughly constant as the voice count changes.
        var norm = sumSq > 1e-9 ? (float)(1.0 / Math.Sqrt(sumSq)) : 1f;
        for (var i = 0; i < _count; i++) _gain[i] *= norm;
    }

    /// <summary>Sets the centre frequency; each voice tracks it at its configured detune ratio.</summary>
    public void SetBaseFrequency(double hz)
    {
        for (var i = 0; i < _count; i++) _oscs[i].SetFrequency(hz * _ratios[i]);
    }

    /// <summary>Produces the next stereo sample, summing all active unison voices.</summary>
    public void Render(out float left, out float right)
    {
        float l = 0, r = 0;
        for (var i = 0; i < _count; i++)
        {
            var s = _oscs[i].Next() * _gain[i];
            l += s * _panL[i];
            r += s * _panR[i];
        }

        left = l;
        right = r;
    }
}
