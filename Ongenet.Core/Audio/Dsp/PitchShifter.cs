using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A single-voice, time-domain pitch shifter: a <see cref="DelayLine"/> read by two taps half a
/// grain-window apart and crossfaded with a Hann window (<see cref="GrainWindow"/>) so the taps sum
/// to unity gain. Quality hinges on the window length: when it's locked to the input's pitch period
/// via <see cref="SetPeriod"/> (PSOLA-style), the two taps sit exactly one period apart, a periodic
/// signal reads near-identically at both, and they add constructively — no comb/warble. Without a
/// period it falls back to a fixed window (fine for non-pitched material). Hold one per channel.
/// </summary>
public sealed class PitchShifter
{
    private readonly DelayLine _delay = new();
    private double _sampleRate = 44100.0;
    private double _minWindow = 88.0;
    private double _maxWindow = 1260.0;
    private double _defaultWindow = 882.0;
    private double _windowSamples = 882.0;  // current (smoothed) grain window length
    private double _targetWindow = 882.0;
    private double _windowCoef;             // one-pole coef for smoothing window changes
    private double _phase;                  // 0..1 sweep position
    private double _ratio = 1.0;            // target / source frequency

    public void Configure(double sampleRate)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 44100.0;
        // Grain = 2 periods, so the two half-window-apart taps sit one period apart. Bound the
        // window to 2 periods across the detector's pitch range (70 Hz..1 kHz).
        _minWindow = Math.Max(64.0, _sampleRate / 1000.0 * 2.0);
        _maxWindow = _sampleRate / 70.0 * 2.0;
        _defaultWindow = _sampleRate * 0.020; // 20 ms when no pitch is known
        _windowSamples = _defaultWindow;
        _targetWindow = _defaultWindow;
        _windowCoef = Math.Exp(-1.0 / (0.015 * _sampleRate)); // ~15 ms glide so window changes don't click
        _delay.Resize((int)Math.Ceiling(_maxWindow) * 2 + 8);
        _phase = 0;
    }

    public void Reset()
    {
        _delay.Clear();
        _phase = 0;
        _windowSamples = _defaultWindow;
        _targetWindow = _defaultWindow;
    }

    /// <summary>Sets the pitch ratio (2.0 = up one octave, 0.5 = down one octave).</summary>
    public void SetRatio(double ratio) => _ratio = ratio <= 0 ? 1.0 : ratio;

    /// <summary>
    /// Locks the grain window to the input pitch period (samples) for clean, comb-free shifting.
    /// Pass a value &lt;= 0 (unvoiced) to fall back to the default fixed window.
    /// </summary>
    public void SetPeriod(double periodSamples)
        => _targetWindow = periodSamples > 0
            ? Math.Clamp(periodSamples * 2.0, _minWindow, _maxWindow)
            : _defaultWindow;

    /// <summary>Shifts one sample.</summary>
    public float Process(float input)
    {
        _delay.Write(input);

        // Glide the window toward its target so the read taps never jump (which would click).
        _windowSamples += (1.0 - _windowCoef) * (_targetWindow - _windowSamples);

        // Reading the delay line at a position moving at (1 - ratio) per sample resamples to the
        // target pitch; the two overlapping, Hann-windowed taps hide the wrap.
        var increment = (1.0 - _ratio) / _windowSamples;
        _phase += increment;
        if (_phase >= 1.0) _phase -= 1.0;
        else if (_phase < 0.0) _phase += 1.0;

        var phase2 = _phase + 0.5;
        if (phase2 >= 1.0) phase2 -= 1.0;

        var d0 = _phase * _windowSamples;
        var d1 = phase2 * _windowSamples;

        var w0 = GrainWindow.Lookup(GrainWindowShape.Hann, _phase);
        var w1 = GrainWindow.Lookup(GrainWindowShape.Hann, phase2);

        return w0 * _delay.ReadFrac(d0) + w1 * _delay.ReadFrac(d1);
    }
}
