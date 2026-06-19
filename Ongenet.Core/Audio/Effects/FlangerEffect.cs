using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A flanger: a short LFO-modulated delay (fractional read) with feedback, mixed with the dry
/// signal. The LFO is offset 90° on the right channel for stereo width.
/// </summary>
public sealed class FlangerEffect : IAudioEffect
{
    public const string TypeId = "flanger";

    string IAudioEffect.TypeId => TypeId;

    private const double BaseMs = 1.0;
    private const double SweepMs = 6.0;
    private const double MaxMs = BaseMs + SweepMs + 2.0;

    public bool Enabled { get; set; } = true;

    public double RateHz { get; set; } = 0.3;
    public double Depth { get; set; } = 0.6;
    public double Feedback { get; set; } = 0.3;
    public double Mix { get; set; } = 0.5;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private DelayLine[] _lines = Array.Empty<DelayLine>();
    private readonly Lfo _lfo = new();

    public string Name => "Flanger";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Rate", 0.05, 5.0, () => RateHz, v => RateHz = v, "0.##", "Hz", 2.0),
        new FloatParameter("Depth", 0.0, 1.0, () => Depth, v => Depth = v),
        new FloatParameter("Feedback", -0.95, 0.95, () => Feedback, v => Feedback = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var size = (int)(MaxMs / 1000.0 * _sampleRate) + 4;
        _lines = new DelayLine[_channels];
        for (var c = 0; c < _channels; c++) { _lines[c] = new DelayLine(); _lines[c].Resize(size); }
        _lfo.Reset();
    }

    public IAudioEffect Clone() => new FlangerEffect
    {
        Enabled = Enabled, RateHz = RateHz, Depth = Depth, Feedback = Feedback, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        if (_lines.Length == 0) return;
        var channels = _channels;
        var fb = (float)Math.Clamp(Feedback, -0.95, 0.95);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var depth = Math.Clamp(Depth, 0, 1);
        _lfo.SetRate(RateHz, _sampleRate);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var lfo = _lfo.Value(c == 1 ? 0.25 : 0.0); // 90° offset on the right channel
                var delayMs = BaseMs + SweepMs * depth * (0.5 + 0.5 * lfo);
                var delaySamp = delayMs / 1000.0 * _sampleRate;

                var delayed = _lines[c].ReadFrac(delaySamp);
                var dry = buffer[i + c];
                buffer[i + c] = dry * (1 - mix) + delayed * mix;
                _lines[c].Write(dry + delayed * fb);
            }

            _lfo.Advance();
        }
    }
}
