using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A phaser: a cascade of first-order allpass stages whose cutoff is swept by an LFO, with feedback,
/// mixed with the dry signal to produce moving notches.
/// </summary>
public sealed class PhaserEffect : IAudioEffect
{
    public const string TypeId = "phaser";

    private const double MinFc = 200.0;
    private const double MaxFc = 2000.0;
    private const int MaxStages = 8;

    private static readonly string[] StageNames = { "2", "4", "6", "8" };
    private static readonly int[] StageCounts = { 2, 4, 6, 8 };

    public bool Enabled { get; set; } = true;

    public double RateHz { get; set; } = 0.5;
    public double Depth { get; set; } = 0.8;
    public double Feedback { get; set; } = 0.4;
    public double Mix { get; set; } = 0.5;
    public int StagesIndex { get; set; } = 1; // → 4 stages

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private double[][] _x1 = Array.Empty<double[]>();
    private double[][] _y1 = Array.Empty<double[]>();
    private double[] _fb = Array.Empty<double>();
    private readonly Lfo _lfo = new();

    public string Name => "Phaser";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Rate", 0.05, 5.0, () => RateHz, v => RateHz = v, "0.##", "Hz", 2.0),
        new FloatParameter("Depth", 0.0, 1.0, () => Depth, v => Depth = v),
        new FloatParameter("Feedback", -0.95, 0.95, () => Feedback, v => Feedback = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new ChoiceParameter("Stages", StageNames, () => StagesIndex, v => StagesIndex = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _x1 = new double[_channels][];
        _y1 = new double[_channels][];
        for (var c = 0; c < _channels; c++) { _x1[c] = new double[MaxStages]; _y1[c] = new double[MaxStages]; }
        _fb = new double[_channels];
        _lfo.Reset();
    }

    public IAudioEffect Clone() => new PhaserEffect
    {
        Enabled = Enabled, RateHz = RateHz, Depth = Depth, Feedback = Feedback, Mix = Mix, StagesIndex = StagesIndex
    };

    public void Process(Span<float> buffer)
    {
        if (_x1.Length == 0) return;
        var channels = _channels;
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var fbAmt = Math.Clamp(Feedback, -0.95, 0.95);
        var depth = Math.Clamp(Depth, 0, 1);
        var stages = StageCounts[Math.Clamp(StagesIndex, 0, StageCounts.Length - 1)];
        _lfo.SetRate(RateHz, _sampleRate);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var lfo = _lfo.Value(c == 1 ? 0.25 : 0.0);
                var fc = MinFc * Math.Pow(MaxFc / MinFc, depth * (0.5 + 0.5 * lfo));
                var t = Math.Tan(Math.PI * fc / _sampleRate);
                var a = (t - 1.0) / (t + 1.0);

                var dry = buffer[i + c];
                var x = dry + fbAmt * _fb[c];
                for (var s = 0; s < stages; s++)
                {
                    var y = a * x + _x1[c][s] - a * _y1[c][s];
                    _x1[c][s] = x;
                    _y1[c][s] = y;
                    x = y;
                }

                _fb[c] = x;
                buffer[i + c] = (float)(dry * (1 - mix) + x * mix);
            }

            _lfo.Advance();
        }
    }
}
