using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>How two Polysynth oscillators are combined.</summary>
public enum PolysynthBlendOp
{
    Mix,
    Neg,
    Wipe,
    Am,
    Sign,
    Max
}

/// <summary>
/// Polysynth-style oscillator unit: shape-blended saw/pulse, sub pulse, optional sync and unison.
/// Reusable by Polysynth instrument and Field nodes.
/// </summary>
public sealed class PolysynthOscillator
{
    private double _phase;
    private double _inc;
    private int _sampleRate = 44100;
    private FastRandom _rng = new(1);

    public double PitchSemitones { get; set; }
    public int OctaveShift { get; set; }
    public double Shape { get; set; } = 0.5;
    public double PulseWidth { get; set; } = 0.5;
    public double SubLevel { get; set; }
    public double SubWidth { get; set; } = 0.5;
    public double SyncSemitones { get; set; }
    public bool SyncReset { get; set; }
    public int Voices { get; set; } = 1;
    public double UnisonCents { get; set; }
    public double Width { get; set; } = 0.5;
    public double Pan { get; set; }
    public double Level { get; set; } = 0.8;

    private double _baseHz = 440;
    private double _masterPhase;

    public void SetSampleRate(int sampleRate) => _sampleRate = sampleRate <= 0 ? 44100 : sampleRate;

    public void SetBaseFrequency(double hz) => _baseHz = Math.Max(1, hz);

    public void Reset()
    {
        _phase = 0;
        _masterPhase = 0;
    }

    public void Seed(uint seed) => _rng = new FastRandom(seed);

    /// <summary>Process one sample; returns mono sum (caller applies pan).</summary>
    public float Process()
    {
        var hz = _baseHz * Math.Pow(2, PitchSemitones / 12.0 + OctaveShift);
        _inc = hz / _sampleRate;

        if (SyncReset && SyncSemitones > 0.01)
        {
            var syncHz = hz * Math.Pow(2, SyncSemitones / 12.0);
            _masterPhase += syncHz / _sampleRate;
            if (_masterPhase >= 1.0) { _masterPhase -= 1.0; _phase = 0; }
        }

        _phase += _inc;
        if (_phase >= 1.0) _phase -= 1.0;

        var voices = Math.Clamp(Voices, 1, 16);
        if (voices == 1)
            return (float)(Level * ShapeSample(_phase, Shape, PulseWidth, SubLevel, SubWidth));

        var sum = 0f;
        for (var v = 0; v < voices; v++)
        {
            var detune = voices <= 1 ? 0.0 : (v / (double)(voices - 1) - 0.5) * 2.0 * UnisonCents / 100.0;
            var p = _phase + detune;
            p -= Math.Floor(p);
            sum += ShapeSample(p, Shape, PulseWidth, SubLevel, SubWidth);
        }

        return (float)(Level * sum / voices);
    }

    public static float Blend(float a, float b, PolysynthBlendOp op, float mix)
    {
        var blended = op switch
        {
            PolysynthBlendOp.Mix => a * (1f - mix) + b * mix,
            PolysynthBlendOp.Neg => a * (1f - mix) - b * mix,
            PolysynthBlendOp.Wipe => a * (1f - mix * mix) + b * (mix * mix),
            PolysynthBlendOp.Am => a * (1f - mix + mix * Math.Abs(b)),
            PolysynthBlendOp.Sign => a * (1f - mix) + Math.Sign(a) * b * mix,
            PolysynthBlendOp.Max => a * (1f - mix) + Math.Max(a, b) * mix,
            _ => a * (1f - mix) + b * mix
        };
        return blended;
    }

    private static float ShapeSample(double phase, double shape, double pw, double subLevel, double subWidth)
    {
        var s = Math.Clamp(shape, 0, 1);
        var saw = 2.0 * phase - 1.0;
        var pulseUp = phase < pw ? 1.0 : -1.0;
        var sawUp = 2.0 * ((phase * 2.0) % 1.0) - 1.0;

        double main;
        if (s < 0.5)
        {
            var t = s * 2.0;
            main = pulseUp * (1.0 - t) + saw * t;
        }
        else
        {
            var t = (s - 0.5) * 2.0;
            main = saw * (1.0 - t) + sawUp * t;
        }

        var sub = (phase * 0.5 % 1.0) < subWidth ? 1.0 : -1.0;
        return (float)(main + subLevel * sub);
    }
}
