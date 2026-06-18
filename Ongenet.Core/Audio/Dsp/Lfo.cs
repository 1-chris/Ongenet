using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>The LFO waveform shapes.</summary>
public enum LfoWave { Sine, Triangle, Saw, Square }

/// <summary>
/// A low-frequency oscillator: a normalised phase accumulator (cycles, [0,1)) producing a [-1,1]
/// waveform. <see cref="Value"/> samples at a phase offset (for stereo spread / multi-voice) without
/// advancing; <see cref="Next"/> samples and advances; <see cref="Advance"/> advances only.
/// </summary>
public sealed class Lfo
{
    private double _phase;
    private double _inc;

    public LfoWave Wave { get; set; } = LfoWave.Sine;

    public double Phase => _phase;

    public void SetRate(double hz, double sampleRate) => _inc = sampleRate > 0 ? hz / sampleRate : 0;

    public void Reset(double phase = 0) => _phase = phase - Math.Floor(phase);

    public double Next()
    {
        var v = Shape(_phase);
        Advance();
        return v;
    }

    public void Advance()
    {
        _phase += _inc;
        if (_phase >= 1.0) _phase -= Math.Floor(_phase);
    }

    /// <summary>Samples the waveform at the current phase plus <paramref name="phaseOffset"/> cycles.</summary>
    public double Value(double phaseOffset)
    {
        var p = _phase + phaseOffset;
        p -= Math.Floor(p);
        return Shape(p);
    }

    private double Shape(double p) => Wave switch
    {
        LfoWave.Triangle => 1.0 - 4.0 * Math.Abs(p - 0.5), // +1 at 0.5, -1 at the ends
        LfoWave.Saw => 2.0 * p - 1.0,
        LfoWave.Square => p < 0.5 ? 1.0 : -1.0,
        _ => Math.Sin(2.0 * Math.PI * p)
    };
}
