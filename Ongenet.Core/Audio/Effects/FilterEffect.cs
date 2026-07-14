using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A multi-mode resonant filter (RBJ biquad): low-pass, band-pass, high-pass, notch, or bypass,
/// with pre/post gain (dB), cutoff frequency and resonance (Q). Optional multibank mode uses
/// <see cref="MultibankGateFilterDsp"/> for resonant band stacks.
/// </summary>
public sealed class FilterEffect : IAudioEffect, ISpectrumSource
{
    public const string TypeId = "filter";

    string IAudioEffect.TypeId => TypeId;

    private static readonly string[] ModeNames = { "Low-pass", "Band-pass", "High-pass", "Notch", "Bypass" };

    public bool Enabled { get; set; } = true;

    public double PreGainDb { get; set; }
    public double PostGainDb { get; set; }
    public double Frequency { get; set; } = 1000.0;
    public double Resonance { get; set; } = 0.7;
    public FilterMode Mode { get; set; } = FilterMode.LowPass;
    public bool MultibankMode { get; set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;

    private Biquad[] _bq = new Biquad[2];
    private MultibankGateFilterDsp[] _multibank = Array.Empty<MultibankGateFilterDsp>();

    private BiquadCoefficients _coeffs = BiquadCoefficients.Identity;
    private double _lastFreq = double.NaN, _lastQ = double.NaN, _lastSr = double.NaN;
    private FilterMode _lastMode = (FilterMode)(-1);

    private readonly SpectrumScope _scope = new();

    public string Name => "Filter";

    public int SampleRate => (int)_sampleRate;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Pre Gain", -24.0, 24.0, () => PreGainDb, v => PreGainDb = v, "0.0", "dB"),
        new FloatParameter("Frequency", 20.0, 20000.0, () => Frequency, v => Frequency = v, "0", "Hz", 3.0),
        new FloatParameter("Resonance", 0.5, 16.0, () => Resonance, v => Resonance = v, "0.0", "Q", 2.0),
        new FloatParameter("Post Gain", -24.0, 24.0, () => PostGainDb, v => PostGainDb = v, "0.0", "dB"),
        new ChoiceParameter("Mode", ModeNames, () => (int)Mode, v => Mode = (FilterMode)v),
        new BoolParameter("Multibank", () => MultibankMode, v => MultibankMode = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _bq = new Biquad[_channels];
        _multibank = new MultibankGateFilterDsp[_channels];
        for (var c = 0; c < _channels; c++)
        {
            _multibank[c] = new MultibankGateFilterDsp();
            _multibank[c].Prepare(_sampleRate);
            _multibank[c].HoldOpen();
        }
        _lastMode = (FilterMode)(-1);
    }

    public IAudioEffect Clone() => new FilterEffect
    {
        Enabled = Enabled,
        PreGainDb = PreGainDb,
        PostGainDb = PostGainDb,
        Frequency = Frequency,
        Resonance = Resonance,
        Mode = Mode,
        MultibankMode = MultibankMode
    };

    public void Process(Span<float> buffer)
    {
        var bq = _bq;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, bq.Length);
        if (channels <= 0) return;
        var mode = Mode;
        if (mode == FilterMode.Bypass) { _scope.Tap(buffer, channels); return; }

        var pre = AudioMath.Db2Lin(PreGainDb);
        var post = AudioMath.Db2Lin(PostGainDb);
        var frames = buffer.Length / channels;

        if (MultibankMode)
        {
            ConfigureMultibank();
            var mb = _multibank;
            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                for (var c = 0; c < channels; c++)
                    buffer[i + c] = mb[c].Process((float)(buffer[i + c] * pre)) * (float)post;
            }
        }
        else
        {
            if (mode != _lastMode || Frequency != _lastFreq || Resonance != _lastQ || _sampleRate != _lastSr)
            {
                _coeffs = BiquadCoefficients.Compute(mode, Frequency, Resonance, _sampleRate);
                _lastMode = mode; _lastFreq = Frequency; _lastQ = Resonance; _lastSr = _sampleRate;
            }

            var coeffs = _coeffs;
            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                for (var c = 0; c < channels; c++)
                {
                    var y = bq[c].Process(coeffs, buffer[i + c] * pre);
                    buffer[i + c] = (float)(y * post);
                }
            }
        }

        _scope.Tap(buffer, channels);
    }

    public int CaptureLatest(float[] dest) => _scope.CaptureLatest(dest);

    private void ConfigureMultibank()
    {
        var freq = Math.Clamp(Frequency, 20, _sampleRate * 0.45);
        var q = Math.Clamp(Resonance, 0.5, 16);
        for (var c = 0; c < _channels; c++)
        {
            var mb = _multibank[c];
            for (var i = 0; i < MultibankGateFilterDsp.BankCount; i++)
            {
                mb.Levels[i] = 0;
                mb.Cutoffs[i] = freq * Math.Pow(2, (i - 3) * 0.25);
                mb.Resonances[i] = q;
            }

            var best = 0;
            var bestDist = double.MaxValue;
            for (var i = 0; i < MultibankGateFilterDsp.BankCount; i++)
            {
                var dist = Math.Abs(Math.Log(mb.Cutoffs[i] / freq));
                if (dist < bestDist) { bestDist = dist; best = i; }
            }

            mb.Levels[best] = 1.0;
        }
    }
}
