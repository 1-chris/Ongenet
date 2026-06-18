using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A chorus: several LFO-modulated medium delays (~12–28 ms, no feedback) summed and mixed with the
/// dry signal for a thickening, detuned ensemble. Wider and gentler than the flanger.
/// </summary>
public sealed class ChorusEffect : IAudioEffect
{
    public const string TypeId = "chorus";

    private const int Voices = 3;
    private const double BaseMs = 12.0;
    private const double SweepMs = 8.0;
    private const double MaxMs = BaseMs + SweepMs + 4.0;

    public bool Enabled { get; set; } = true;

    public double RateHz { get; set; } = 0.5;
    public double Depth { get; set; } = 0.6;
    public double Mix { get; set; } = 0.5;
    public double Spread { get; set; } = 0.7;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private DelayLine[] _lines = Array.Empty<DelayLine>();
    private readonly Lfo _lfo = new();

    public string Name => "Chorus";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Rate", 0.05, 5.0, () => RateHz, v => RateHz = v, "0.##", "Hz", 2.0),
        new FloatParameter("Depth", 0.0, 1.0, () => Depth, v => Depth = v),
        new FloatParameter("Spread", 0.0, 1.0, () => Spread, v => Spread = v),
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

    public IAudioEffect Clone() => new ChorusEffect
    {
        Enabled = Enabled, RateHz = RateHz, Depth = Depth, Mix = Mix, Spread = Spread
    };

    public void Process(Span<float> buffer)
    {
        if (_lines.Length == 0) return;
        var channels = _channels;
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var depth = Math.Clamp(Depth, 0, 1);
        var spread = Math.Clamp(Spread, 0, 1);
        _lfo.SetRate(RateHz, _sampleRate);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var chanOffset = c == 1 ? spread * 0.5 : 0.0; // stereo de-correlation
                double wet = 0;
                for (var v = 0; v < Voices; v++)
                {
                    var lfo = _lfo.Value(chanOffset + (double)v / Voices);
                    var delayMs = BaseMs + SweepMs * depth * (0.5 + 0.5 * lfo);
                    wet += _lines[c].ReadFrac(delayMs / 1000.0 * _sampleRate);
                }

                wet /= Voices;
                var dry = buffer[i + c];
                buffer[i + c] = (float)(dry * (1 - mix) + wet * mix);
                _lines[c].Write(dry);
            }

            _lfo.Advance();
        }
    }
}
