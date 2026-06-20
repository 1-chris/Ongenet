namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A gated six-stage Delay→Attack→Hold→Decay→Sustain→Release envelope, processed one sample at a time.
/// This is the full SFZ-style <c>ampeg</c>/<c>pitcheg</c>/<c>fileg</c> shape: it generalises the simpler
/// <see cref="Ongenet.Core.Audio.Instruments.AdsrEnvelope"/> with pre-attack delay and a hold plateau,
/// and unlike the stateless <see cref="CurveEnvelope"/> it sustains until <see cref="Release"/> is called.
/// Times are in seconds; <see cref="SustainLevel"/> is a level in [0, 1].
/// </summary>
public sealed class DahdsrEnvelope
{
    private enum Stage { Idle, Delay, Attack, Hold, Decay, Sustain, Release }

    private Stage _stage = Stage.Idle;
    private int _sampleRate = 44100;
    private double _level;        // current output level
    private double _releaseStart; // level captured when release began
    private int _delayLeft;       // samples remaining in the delay stage
    private int _holdLeft;        // samples remaining in the hold stage

    public double DelaySeconds { get; set; }
    public double AttackSeconds { get; set; } = 0.001;
    public double HoldSeconds { get; set; }
    public double DecaySeconds { get; set; }
    public double SustainLevel { get; set; } = 1.0;
    public double ReleaseSeconds { get; set; } = 0.005;

    /// <summary>The current output level [0, 1] without advancing (for control-rate modulation reads).</summary>
    public double Level => _level;

    /// <summary>True while the envelope is producing (or about to produce) sound.</summary>
    public bool IsActive => _stage != Stage.Idle;

    /// <summary>True once <see cref="Release"/> has been called and the tail is fading.</summary>
    public bool IsReleasing => _stage == Stage.Release;

    public void SetSampleRate(int sampleRate) => _sampleRate = sampleRate <= 0 ? 44100 : sampleRate;

    /// <summary>Begins a new note from silence (enters the delay/attack stage).</summary>
    public void Gate()
    {
        _level = 0.0;
        _delayLeft = Seconds(DelaySeconds);
        _holdLeft = Seconds(HoldSeconds);
        _stage = _delayLeft > 0 ? Stage.Delay : Stage.Attack;
    }

    /// <summary>Releases the note (enters the release stage, fading from the current level).</summary>
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
            case Stage.Delay:
                if (--_delayLeft <= 0) _stage = Stage.Attack;
                _level = 0.0;
                break;

            case Stage.Attack:
                _level += StepFor(AttackSeconds);
                if (_level >= 1.0)
                {
                    _level = 1.0;
                    _stage = _holdLeft > 0 ? Stage.Hold : Stage.Decay;
                }
                break;

            case Stage.Hold:
                _level = 1.0;
                if (--_holdLeft <= 0) _stage = Stage.Decay;
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

    private int Seconds(double seconds)
    {
        var n = (int)(seconds * _sampleRate);
        return n < 0 ? 0 : n;
    }
}
