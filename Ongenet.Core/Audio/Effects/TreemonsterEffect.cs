using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Ring modulation plus mild pitch wobble: a sine carrier at Rate multiplies the input while an
/// LFO-modulated delay line adds a subtle pitch flutter.
/// </summary>
public sealed class TreemonsterEffect : IAudioEffect
{
    public const string TypeId = "treemonster";

    string IAudioEffect.TypeId => TypeId;

    private const double WobbleMs = 3.0;

    public bool Enabled { get; set; } = true;

    public double RateHz { get; set; } = 5.0;
    public double Amount { get; set; } = 0.5;
    public double Mix { get; set; } = 0.5;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private RingModulator[] _ring = Array.Empty<RingModulator>();
    private DelayLine[] _wobble = Array.Empty<DelayLine>();
    private readonly Lfo _lfo = new();
    private double _wobbleCenter;

    public string Name => "Treemonster";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Rate", 0.1, 30.0, () => RateHz, v => RateHz = v, "0.##", "Hz", 2.0),
        new FloatParameter("Amount", 0.0, 1.0, () => Amount, v => Amount = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var ring = new RingModulator[_channels];
        var wobble = new DelayLine[_channels];
        var size = (int)(WobbleMs * 2.0 / 1000.0 * _sampleRate) + 8;
        for (var c = 0; c < _channels; c++)
        {
            ring[c] = new RingModulator();
            wobble[c] = new DelayLine();
            wobble[c].Resize(size);
        }

        _ring = ring;
        _wobble = wobble;
        _wobbleCenter = WobbleMs / 1000.0 * _sampleRate;
        _lfo.Reset();
    }

    public IAudioEffect Clone() => new TreemonsterEffect
    {
        Enabled = Enabled, RateHz = RateHz, Amount = Amount, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var ring = _ring;
        var wobble = _wobble;
        if (ring.Length == 0 || wobble.Length == 0) return;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, Math.Min(ring.Length, wobble.Length));
        var amount = (float)Math.Clamp(Amount, 0, 1);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var wobbleDepth = _wobbleCenter * amount * 0.4;
        _lfo.SetRate(RateHz * 0.7, _sampleRate);

        for (var c = 0; c < channels; c++)
            ring[c].Configure(RateHz, (int)_sampleRate);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var lfo = _lfo.Value(0);
            var delay = _wobbleCenter + wobbleDepth * lfo;
            var i = frame * channels;

            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                wobble[c].Write(dry);
                var flutter = wobble[c].ReadFrac(delay);
                var modulated = dry + (flutter - dry) * amount * 0.35f;
                ring[c].Mix = amount;
                var wet = ring[c].Process(modulated);
                buffer[i + c] = dry * (1 - mix) + wet * mix;
            }

            _lfo.Advance();
        }
    }
}
