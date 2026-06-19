using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A multi-mode resonant filter (RBJ biquad): low-pass, band-pass, high-pass, notch, or bypass,
/// with pre/post gain (dB), cutoff frequency and resonance (Q). Processes interleaved stereo in
/// place, with independent per-channel filter state.
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

    private int _channels = 2;
    private double _sampleRate = 44100.0;

    // Per-channel biquad state.
    private Biquad[] _bq = new Biquad[2];

    // Cached coefficients (recomputed when the controlling parameters change).
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
        new ChoiceParameter("Mode", ModeNames, () => (int)Mode, v => Mode = (FilterMode)v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _bq = new Biquad[_channels];
        _lastMode = (FilterMode)(-1); // force coefficient recompute
    }

    public IAudioEffect Clone() => new FilterEffect
    {
        Enabled = Enabled,
        PreGainDb = PreGainDb,
        PostGainDb = PostGainDb,
        Frequency = Frequency,
        Resonance = Resonance,
        Mode = Mode
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        var mode = Mode;
        if (mode == FilterMode.Bypass) { _scope.Tap(buffer, channels); return; } // transparent

        if (_bq.Length < channels) Prepare(new AudioFormat((int)_sampleRate, channels));

        if (mode != _lastMode || Frequency != _lastFreq || Resonance != _lastQ || _sampleRate != _lastSr)
        {
            _coeffs = BiquadCoefficients.Compute(mode, Frequency, Resonance, _sampleRate);
            _lastMode = mode; _lastFreq = Frequency; _lastQ = Resonance; _lastSr = _sampleRate;
        }

        var pre = AudioMath.Db2Lin(PreGainDb);
        var post = AudioMath.Db2Lin(PostGainDb);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var y = _bq[c].Process(_coeffs, buffer[i + c] * pre);
                buffer[i + c] = (float)(y * post);
            }
        }

        _scope.Tap(buffer, channels);
    }

    public int CaptureLatest(float[] dest) => _scope.CaptureLatest(dest);
}
