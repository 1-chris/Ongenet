using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Frequency shifter+ via Hilbert SSB (<see cref="FreqShifter"/>) with feedback and LFO-modulated shift.
/// </summary>
public sealed class FreqShiftPlusEffect : IAudioEffect
{
    public const string TypeId = "freq_shifter_plus";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double FrequencyHz { get; set; } = 100.0;
    public double Mix { get; set; } = 1.0;
    public double Feedback { get; set; }
    public double ModRateHz { get; set; } = 0.25;
    public double ModDepthHz { get; set; } = 50.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private FreqShifter[] _shifters = Array.Empty<FreqShifter>();
    private double[] _fb = Array.Empty<double>();
    private readonly Lfo _lfo = new();

    public string Name => "Freq Shifter+";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Frequency", -4000.0, 4000.0, () => FrequencyHz, v => FrequencyHz = v, "0", "Hz"),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new FloatParameter("Feedback", -0.9, 0.9, () => Feedback, v => Feedback = v),
        new FloatParameter("Mod Rate", 0.02, 8.0, () => ModRateHz, v => ModRateHz = v, "0.##", "Hz", 2.0),
        new FloatParameter("Mod Depth", 0.0, 500.0, () => ModDepthHz, v => ModDepthHz = v, "0", "Hz")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var shifters = new FreqShifter[_channels];
        for (var c = 0; c < _channels; c++)
        {
            shifters[c] = new FreqShifter();
            shifters[c].Configure(FrequencyHz, (int)_sampleRate);
            shifters[c].Reset();
        }

        _shifters = shifters;
        _fb = new double[_channels];
        _lfo.Reset();
    }

    public IAudioEffect Clone() => new FreqShiftPlusEffect
    {
        Enabled = Enabled, FrequencyHz = FrequencyHz, Mix = Mix, Feedback = Feedback,
        ModRateHz = ModRateHz, ModDepthHz = ModDepthHz
    };

    public void Process(Span<float> buffer)
    {
        var shifters = _shifters;
        var fb = _fb;
        if (shifters.Length == 0 || fb.Length == 0) return;

        var channels = Math.Min(_channels < 1 ? 1 : _channels, Math.Min(shifters.Length, fb.Length));
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var fbAmt = Math.Clamp(Feedback, -0.9, 0.9);
        var modDepth = Math.Clamp(ModDepthHz, 0, 500);
        _lfo.SetRate(ModRateHz, _sampleRate);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var mod = modDepth * _lfo.Value(c == 1 ? 0.25 : 0.0);
                shifters[c].Configure(FrequencyHz + mod, (int)_sampleRate);

                var dry = buffer[i + c];
                var x = dry + fbAmt * fb[c];
                var wet = shifters[c].Process((float)x);
                fb[c] = wet;
                buffer[i + c] = dry * (1 - mix) + wet * mix;
            }

            _lfo.Advance();
        }
    }
}
