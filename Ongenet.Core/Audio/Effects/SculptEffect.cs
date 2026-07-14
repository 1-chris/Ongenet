using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Multi-band spectral shaper: a log-spaced filter bank with per-band gains driven by a macro
/// Shape control (tilt from bass-heavy to treble-heavy) plus individual Low/Mid/High trims.
/// </summary>
public sealed class SculptEffect : IAudioEffect
{
    public const string TypeId = "sculpt";

    string IAudioEffect.TypeId => TypeId;

    private const int Bands = 8;

    public bool Enabled { get; set; } = true;

    public double Shape { get; set; }
    public double Low { get; set; } = 1.0;
    public double Mid { get; set; } = 1.0;
    public double High { get; set; } = 1.0;
    public double Mix { get; set; } = 1.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private HarmonicSculptor[] _sculptors = Array.Empty<HarmonicSculptor>();
    private double _lastShape = double.NaN, _lastLow = double.NaN, _lastMid = double.NaN, _lastHigh = double.NaN;

    public string Name => "Sculpt";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Shape", -1.0, 1.0, () => Shape, v => Shape = v),
        new FloatParameter("Low", 0.0, 2.0, () => Low, v => Low = v, "0.##"),
        new FloatParameter("Mid", 0.0, 2.0, () => Mid, v => Mid = v, "0.##"),
        new FloatParameter("High", 0.0, 2.0, () => High, v => High = v, "0.##"),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var sculptors = new HarmonicSculptor[_channels];
        for (var c = 0; c < _channels; c++)
        {
            sculptors[c] = new HarmonicSculptor();
            sculptors[c].Configure(Bands, (int)_sampleRate);
        }

        _sculptors = sculptors;
        _lastShape = double.NaN;
    }

    public IAudioEffect Clone() => new SculptEffect
    {
        Enabled = Enabled, Shape = Shape, Low = Low, Mid = Mid, High = High, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var sculptors = _sculptors;
        if (sculptors.Length == 0) return;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, sculptors.Length);
        var shape = Math.Clamp(Shape, -1, 1);
        var low = Math.Max(0, Low);
        var mid = Math.Max(0, Mid);
        var high = Math.Max(0, High);

        if (shape != _lastShape || low != _lastLow || mid != _lastMid || high != _lastHigh)
        {
            for (var c = 0; c < channels; c++)
            {
                var sc = sculptors[c];
                for (var b = 0; b < Bands; b++)
                {
                    var t = Bands > 1 ? (double)b / (Bands - 1) : 0.5;
                    var tilt = 1.0 + shape * (0.5 - t) * 1.5;
                    var zone = t < 0.33 ? low : t < 0.66 ? mid : high;
                    sc.SetBandGain(b, tilt * zone);
                }
            }

            _lastShape = shape;
            _lastLow = low;
            _lastMid = mid;
            _lastHigh = high;
        }

        var mix = (float)Math.Clamp(Mix, 0, 1);
        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var wet = sculptors[c].Process(dry);
                buffer[i + c] = dry * (1 - mix) + wet * mix;
            }
        }
    }
}
