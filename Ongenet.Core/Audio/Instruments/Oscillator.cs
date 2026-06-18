using System;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A single phase-accumulating oscillator. Generates one sample per <see cref="Next"/> call
/// for the configured <see cref="Waveform"/> and frequency.
/// </summary>
/// <remarks>
/// The waveforms are naive (not band-limited), so very high notes will alias. This is fine for
/// a first instrument; a future step can add PolyBLEP/band-limiting behind this same API
/// without changing callers.
/// </remarks>
public sealed class Oscillator
{
    private double _phase;          // normalised phase in [0, 1)
    private double _phaseIncrement; // phase advanced per sample
    private int _sampleRate = 44100;

    public Waveform Waveform { get; set; } = Waveform.Sine;

    public void SetSampleRate(int sampleRate)
    {
        _sampleRate = sampleRate <= 0 ? 44100 : sampleRate;
    }

    public void SetFrequency(double hz)
    {
        _phaseIncrement = hz / _sampleRate;
    }

    /// <summary>Resets the phase to the start of the cycle (call on note start).</summary>
    public void ResetPhase() => _phase = 0.0;

    /// <summary>Produces the next sample in [-1, 1] and advances the phase.</summary>
    public float Next()
    {
        var value = Waveform switch
        {
            Waveform.Sine => Math.Sin(_phase * 2.0 * Math.PI),
            Waveform.Sawtooth => 2.0 * _phase - 1.0,
            Waveform.Square => _phase < 0.5 ? 1.0 : -1.0,
            _ => 0.0
        };

        _phase += _phaseIncrement;
        if (_phase >= 1.0) _phase -= 1.0;

        return (float)value;
    }
}
