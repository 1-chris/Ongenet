using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Karplus-Strong plucked string synthesis. Shared by sampler presets and Field nodes.
/// </summary>
public sealed class KarplusStrongDsp
{
    private float[] _delay = Array.Empty<float>();
    private int _pos;
    private double _sampleRate = 44100.0;
    private float _excitation;
    private float _decay = 0.996f;
    private readonly OnePole _damp = new();

    public double Damping { get; set; } = 0.5;
    public double PickPosition { get; set; } = 0.5;
    public double Brightness { get; set; } = 0.5;

    public void Prepare(double sampleRate)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 44100.0;
        _damp.Reset();
        _damp.SetLowpass(4000, _sampleRate);
    }

    public void SetFrequency(double hz)
    {
        var period = (int)Math.Clamp(_sampleRate / Math.Max(hz, 20.0), 8, 8192);
        if (_delay.Length != period)
        {
            _delay = new float[period];
            _pos = 0;
        }
        _decay = (float)(0.992 + (1.0 - Math.Clamp(Damping, 0, 1)) * 0.006);
    }

    public void Pluck(float excitation = 1f) => _excitation = excitation;

    public float Process()
    {
        if (_delay.Length == 0) return 0f;

        if (_excitation > 1e-6f)
        {
            var pick = (int)(PickPosition * _delay.Length) % _delay.Length;
            _delay[pick] += _excitation;
            _excitation = 0f;
        }

        var sample = _delay[_pos];
        var dampHz = 200.0 + Brightness * 8000.0;
        _damp.SetLowpass(dampHz, _sampleRate);
        var filtered = (float)_damp.ProcessLP(sample);
        _delay[_pos] = filtered * _decay;

        _pos++;
        if (_pos >= _delay.Length) _pos = 0;
        return sample;
    }

    public bool IsSilent(float threshold = 1e-5f)
    {
        foreach (var s in _delay)
            if (Math.Abs(s) > threshold) return false;
        return true;
    }
}
