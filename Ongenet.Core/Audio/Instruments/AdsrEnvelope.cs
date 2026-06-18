namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A classic Attack-Decay-Sustain-Release amplitude envelope, processed one sample at a time.
/// Times are in seconds; sustain is a level in [0, 1].
/// </summary>
public sealed class AdsrEnvelope
{
    private enum Stage { Idle, Attack, Decay, Sustain, Release }

    private Stage _stage = Stage.Idle;
    private int _sampleRate = 44100;
    private double _level;        // current output level
    private double _releaseStart; // level when release began

    public double AttackSeconds { get; set; } = 0.005;
    public double DecaySeconds { get; set; } = 0.08;
    public double SustainLevel { get; set; } = 0.7;
    public double ReleaseSeconds { get; set; } = 0.15;

    /// <summary>True while the envelope is producing sound.</summary>
    public bool IsActive => _stage != Stage.Idle;

    public void SetSampleRate(int sampleRate) => _sampleRate = sampleRate <= 0 ? 44100 : sampleRate;

    /// <summary>Begins a new note (enters the attack stage from silence).</summary>
    public void Gate()
    {
        _level = 0.0;
        _stage = Stage.Attack;
    }

    /// <summary>Releases the note (enters the release stage).</summary>
    public void Release()
    {
        if (_stage == Stage.Idle) return;
        _releaseStart = _level;
        _stage = Stage.Release;
    }

    /// <summary>Advances the envelope by one sample and returns the new level in [0, 1].</summary>
    public float Process()
    {
        switch (_stage)
        {
            case Stage.Attack:
                _level += StepFor(AttackSeconds);
                if (_level >= 1.0)
                {
                    _level = 1.0;
                    _stage = Stage.Decay;
                }
                break;

            case Stage.Decay:
                _level -= StepFor(DecaySeconds) * (1.0 - SustainLevel);
                if (_level <= SustainLevel)
                {
                    _level = SustainLevel;
                    _stage = Stage.Sustain;
                }
                break;

            case Stage.Sustain:
                _level = SustainLevel;
                break;

            case Stage.Release:
                _level -= StepFor(ReleaseSeconds) * _releaseStart;
                if (_level <= 0.0)
                {
                    _level = 0.0;
                    _stage = Stage.Idle;
                }
                break;
        }

        return (float)_level;
    }

    // Per-sample increment that traverses a full 0..1 span over the given number of seconds.
    private double StepFor(double seconds)
    {
        var samples = seconds * _sampleRate;
        return samples <= 1.0 ? 1.0 : 1.0 / samples;
    }
}
