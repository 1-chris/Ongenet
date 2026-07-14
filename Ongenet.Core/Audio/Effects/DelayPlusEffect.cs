using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Multitap delay with LFO time modulation and a feedback-path low-pass filter.
/// </summary>
public sealed class DelayPlusEffect : IAudioEffect
{
    public const string TypeId = "delay_plus";

    string IAudioEffect.TypeId => TypeId;

    private const int TapCount = 3;
    private const double MaxDelaySeconds = 2.0;

    public bool Enabled { get; set; } = true;

    public double TimeMs { get; set; } = 350.0;
    public double Feedback { get; set; } = 0.4;
    public double Mix { get; set; } = 0.4;
    public double ModRateHz { get; set; } = 0.4;
    public double ModDepthMs { get; set; } = 4.0;
    public double FilterHz { get; set; } = 4500.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private DelayLine[] _lines = Array.Empty<DelayLine>();
    private Biquad[] _fbFilter = Array.Empty<Biquad>();
    private BiquadCoefficients _fbCoeffs = BiquadCoefficients.Identity;
    private double _lastFilter = double.NaN, _lastSr = double.NaN;
    private readonly Lfo _lfo = new();

    public string Name => "Delay+";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Time", 10.0, 2000.0, () => TimeMs, v => TimeMs = v, "0", "ms", 2.0),
        new FloatParameter("Feedback", 0.0, 0.95, () => Feedback, v => Feedback = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v),
        new FloatParameter("Mod Rate", 0.05, 5.0, () => ModRateHz, v => ModRateHz = v, "0.##", "Hz", 2.0),
        new FloatParameter("Mod Depth", 0.0, 20.0, () => ModDepthMs, v => ModDepthMs = v, "0.#", "ms"),
        new FloatParameter("Filter", 200.0, 16000.0, () => FilterHz, v => FilterHz = v, "0", "Hz", 2.0)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var size = (int)(MaxDelaySeconds * _sampleRate) + 4;
        var lines = new DelayLine[_channels];
        var fbFilter = new Biquad[_channels];
        for (var c = 0; c < _channels; c++) { lines[c] = new DelayLine(); lines[c].Resize(size); }
        _lines = lines;
        _fbFilter = fbFilter;
        _lfo.Reset();
        _lastFilter = double.NaN;
    }

    public IAudioEffect Clone() => new DelayPlusEffect
    {
        Enabled = Enabled, TimeMs = TimeMs, Feedback = Feedback, Mix = Mix,
        ModRateHz = ModRateHz, ModDepthMs = ModDepthMs, FilterHz = FilterHz
    };

    public void Process(Span<float> buffer)
    {
        var lines = _lines;
        var fbFilter = _fbFilter;
        if (lines.Length == 0 || fbFilter.Length == 0) return;
        var channels = Math.Min(_channels, Math.Min(lines.Length, fbFilter.Length));
        var lineSize = lines[0].Size;
        if (lineSize <= 1) return;

        if (FilterHz != _lastFilter || _sampleRate != _lastSr)
        {
            _fbCoeffs = BiquadCoefficients.Compute(FilterMode.LowPass, FilterHz, 0.707, _sampleRate);
            _lastFilter = FilterHz;
            _lastSr = _sampleRate;
        }

        _lfo.SetRate(ModRateHz, _sampleRate);
        var coeffs = _fbCoeffs;
        var baseDelay = TimeMs / 1000.0 * _sampleRate;
        var modDepth = ModDepthMs / 1000.0 * _sampleRate;
        var fb = (float)Math.Clamp(Feedback, 0, 0.95);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var frames = buffer.Length / channels;

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var mod = _lfo.Next();
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                float wet = 0;
                for (var t = 0; t < TapCount; t++)
                {
                    var frac = (t + 1) / (double)TapCount;
                    var delay = baseDelay * frac + mod * modDepth * (c == 1 ? -0.5 : 0.5);
                    delay = Math.Clamp(delay, 1.0, lineSize - 1);
                    wet += lines[c].ReadFrac(delay);
                }

                wet /= TapCount;
                buffer[i + c] = dry * (1 - mix) + wet * mix;
                var fbSample = (float)fbFilter[c].Process(coeffs, dry + wet * fb);
                lines[c].Write(fbSample);
            }
        }
    }
}
