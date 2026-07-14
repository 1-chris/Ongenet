using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// 16-step monophonic step sequencer clocked from host tempo. Drives acid-bass note/accent/slide/tie flags.
/// </summary>
public sealed class BassStepSequencer
{
    public const int StepCount = 16;

    public bool Enabled { get; set; }
    public double TempoBpm { get; set; } = 120.0;
    /// <summary>0 = 1/16, 1 = 1/8, 2 = 1/4 note steps.</summary>
    public int RateIndex { get; set; }

    public readonly BassStep[] Steps = new BassStep[StepCount];

    private double _sampleRate = 44100.0;
    private double _sampleClock;
    private int _currentStep = -1;
    private bool _running;

    public BassStepSequencer()
    {
        for (var i = 0; i < StepCount; i++)
            Steps[i] = new BassStep { Note = 36 + (i % 8) };
    }

    public void SetSampleRate(double sampleRate) => _sampleRate = sampleRate > 0 ? sampleRate : 44100.0;

    public void Reset()
    {
        _sampleClock = 0;
        _currentStep = -1;
        _running = false;
    }

    public void Start() => _running = true;

    public void Stop()
    {
        _running = false;
        _currentStep = -1;
    }

    /// <summary>Advance clock; returns a step trigger when a new step fires.</summary>
    public bool TryAdvance(int frames, out BassStepTrigger trigger)
    {
        trigger = default;
        if (!Enabled || !_running || TempoBpm <= 0 || _sampleRate <= 0) return false;

        var beatsPerStep = RateIndex switch { 1 => 0.5, 2 => 1.0, _ => 0.25 };
        var samplesPerStep = 60.0 / TempoBpm * beatsPerStep * _sampleRate;
        if (samplesPerStep < 1) return false;

        _sampleClock += frames;
        var stepIndex = (int)(_sampleClock / samplesPerStep) % StepCount;
        if (stepIndex == _currentStep) return false;

        _currentStep = stepIndex;
        var step = Steps[stepIndex];
        if (!step.Active) return false;

        trigger = new BassStepTrigger(step.Note, step.Velocity, step.Accent, step.Slide, step.Tie);
        return true;
    }
}

public sealed class BassStep
{
    public bool Active { get; set; } = true;
    public int Note { get; set; } = 36;
    public float Velocity { get; set; } = 0.85f;
    public bool Accent { get; set; }
    public bool Slide { get; set; }
    public bool Tie { get; set; }
}

public readonly record struct BassStepTrigger(int Note, float Velocity, bool Accent, bool Slide, bool Tie);
