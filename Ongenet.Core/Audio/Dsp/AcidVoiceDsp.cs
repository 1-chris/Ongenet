using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Shared acid-bass voice: resonant low-pass on a saw/square oscillator with a clean sine sub
/// bypass, accent envelope on filter cutoff, and soft drive. Used by <see cref="Instruments.BassSynthInstrument"/>
/// acid mode and Field acid patches.
/// </summary>
public sealed class AcidVoiceDsp
{
    private readonly WaveOscillator _osc = new() { Wave = OscWave.Saw };
    private readonly WaveOscillator _sub = new() { Wave = OscWave.Sine };
    private readonly DahdsrEnvelope _accent = new();
    private readonly DahdsrEnvelope _amp = new();
    private readonly DahdsrEnvelope _filt = new();
    private readonly Biquad _filter = new();
    private double _sampleRate = 44100.0;
    private double _freq = 440.0;
    private double _targetFreq = 440.0;
    private double _slideCoeff = 0.01;
    private float _velocity = 1f;
    private bool _accented;
    private bool _accentGate;

    public double Cutoff { get; set; } = 260;
    public double Resonance { get; set; } = 5.0;
    public double SubLevel { get; set; } = 0.55;
    public double Drive { get; set; } = 1.6;
    public double OutputGain { get; set; } = 0.22;
    public double AccentAmount { get; set; } = 0.35;
    public double SlideMs { get; set; } = 60.0;

    public void SetSampleRate(double sampleRate)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 44100.0;
        _osc.SetSampleRate((int)_sampleRate);
        _sub.SetSampleRate((int)_sampleRate);
        _accent.SetSampleRate((int)_sampleRate);
        _accent.AttackSeconds = 0.001;
        _accent.DecaySeconds = 0.18;
        _accent.SustainLevel = 0.0;
        _accent.ReleaseSeconds = 0.01;
        _amp.SetSampleRate((int)_sampleRate);
        _filt.SetSampleRate((int)_sampleRate);
        UpdateSlideCoeff();
    }

    public void ConfigureEnvelopes(
        double ampAttack, double ampDecay, double ampSustain, double ampRelease,
        double filtAttack, double filtDecay, double filtSustain, double filtRelease)
    {
        _amp.AttackSeconds = ampAttack;
        _amp.DecaySeconds = ampDecay;
        _amp.SustainLevel = ampSustain;
        _amp.ReleaseSeconds = ampRelease;
        _filt.AttackSeconds = filtAttack;
        _filt.DecaySeconds = filtDecay;
        _filt.SustainLevel = filtSustain;
        _filt.ReleaseSeconds = filtRelease;
    }

    public void Trigger(int midiNote, float velocity, bool accent, bool slide, bool tie)
    {
        _velocity = velocity;
        _accented = accent;
        var freq = MusicalMath.NoteToFrequency(midiNote);
        if (slide && _freq > 20)
            _targetFreq = freq;
        else
        {
            _freq = freq;
            _targetFreq = freq;
            _osc.SetFrequency(_freq);
            _sub.SetFrequency(_freq);
            if (!tie)
            {
                _osc.ResetPhase();
                _amp.Gate();
                _filt.Gate();
            }
        }

        if (accent)
        {
            _accentGate = true;
            _accent.Gate();
        }
    }

    public void Release()
    {
        _amp.Release();
        _filt.Release();
    }

    public bool IsActive => _amp.IsActive;

    public float Process()
    {
        if (Math.Abs(_freq - _targetFreq) > 0.01)
        {
            _freq += (_targetFreq - _freq) * _slideCoeff;
            _osc.SetFrequency(_freq);
            _sub.SetFrequency(_freq);
        }

        var osc = _osc.Next();
        var sub = _sub.Next() * (float)SubLevel;
        var accent = _accented && _accentGate ? (float)(_accent.Process() * AccentAmount) : 0f;
        if (_accented && _accentGate && !_accent.IsActive) _accentGate = false;
        var fEnv = (float)_filt.Process();
        var cutoff = AudioMath.Clamp(Cutoff * (1.0 + accent + fEnv * 2.5), 20.0, _sampleRate * 0.45);
        var coeffs = BiquadCoefficients.Compute(FilterMode.LowPass, cutoff, Resonance, _sampleRate);
        var filtered = (float)_filter.Process(coeffs, osc);
        var driven = WaveShaper.Shape(filtered, ShaperType.Tanh, (float)(1.0 + Drive));
        var mixed = driven + sub;
        return mixed * (float)(_amp.Process() * OutputGain * _velocity);
    }

    private void UpdateSlideCoeff()
    {
        var ms = Math.Clamp(SlideMs, 1.0, 2000.0);
        var samples = ms * 0.001 * _sampleRate;
        _slideCoeff = samples > 1 ? 1.0 / samples : 1.0;
    }
}
