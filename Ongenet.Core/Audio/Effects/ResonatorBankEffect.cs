using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A bank of parallel resonant band-pass filters tuned to harmonics of a root frequency.
/// Decay controls resonance (Q); Mix blends the ringing bank with the dry signal.
/// </summary>
public sealed class ResonatorBankEffect : IAudioEffect
{
    public const string TypeId = "resonator_bank";

    string IAudioEffect.TypeId => TypeId;

    private const int Harmonics = 8;

    public bool Enabled { get; set; } = true;

    public double Decay { get; set; } = 0.5;
    public double Mix { get; set; } = 0.5;
    public double RootHz { get; set; } = 220.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[][] _state = Array.Empty<Biquad[]>();
    private BiquadCoefficients[] _coeffs = Array.Empty<BiquadCoefficients>();
    private double _lastDecay = double.NaN, _lastRoot = double.NaN, _lastSr = double.NaN;

    public string Name => "Resonator Bank";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Decay", 0.0, 1.0, () => Decay, v => Decay = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new FloatParameter("Root", 20.0, 2000.0, () => RootHz, v => RootHz = v, "0", "Hz", 2.0)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var state = new Biquad[_channels][];
        for (var c = 0; c < _channels; c++)
        {
            state[c] = new Biquad[Harmonics];
            for (var h = 0; h < Harmonics; h++) state[c][h].Reset();
        }

        _state = state;
        _coeffs = new BiquadCoefficients[Harmonics];
        _lastDecay = double.NaN;
    }

    public IAudioEffect Clone() => new ResonatorBankEffect
    {
        Enabled = Enabled, Decay = Decay, Mix = Mix, RootHz = RootHz
    };

    public void Process(Span<float> buffer)
    {
        var state = _state;
        if (state.Length == 0) return;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, state.Length);
        var decay = Math.Clamp(Decay, 0, 1);
        var root = Math.Max(20.0, RootHz);

        if (decay != _lastDecay || root != _lastRoot || _sampleRate != _lastSr)
        {
            var q = 0.5 + decay * 19.5;
            for (var h = 0; h < Harmonics; h++)
            {
                var freq = root * (h + 1);
                if (freq >= _sampleRate * 0.45) break;
                _coeffs[h] = BiquadCoefficients.Compute(FilterMode.BandPass, freq, q, _sampleRate);
            }

            _lastDecay = decay;
            _lastRoot = root;
            _lastSr = _sampleRate;
        }

        var mix = (float)Math.Clamp(Mix, 0, 1);
        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var wet = 0f;
                var bq = state[c];
                for (var h = 0; h < Harmonics; h++)
                {
                    if (root * (h + 1) >= _sampleRate * 0.45) break;
                    wet += (float)bq[h].Process(_coeffs[h], dry);
                }

                wet /= Harmonics;
                buffer[i + c] = dry * (1 - mix) + wet * mix;
            }
        }
    }
}
