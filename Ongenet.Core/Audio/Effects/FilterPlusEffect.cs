using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Resonant biquad filter followed by waveshaper drive: Cutoff, Resonance, Drive, filter Mode,
/// and dry/wet Mix.
/// </summary>
public sealed class FilterPlusEffect : IAudioEffect
{
    public const string TypeId = "filter_plus";

    string IAudioEffect.TypeId => TypeId;

    private static readonly string[] ModeNames = { "Low-pass", "Band-pass", "High-pass" };
    private static readonly FilterMode[] Modes = { FilterMode.LowPass, FilterMode.BandPass, FilterMode.HighPass };

    public bool Enabled { get; set; } = true;

    public double Cutoff { get; set; } = 1000.0;
    public double Resonance { get; set; } = 0.7;
    public double Drive { get; set; } = 4.0;
    public int Mode { get; set; }
    public double Mix { get; set; } = 1.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _bq = Array.Empty<Biquad>();
    private BiquadCoefficients _coeffs = BiquadCoefficients.Identity;
    private double _lastCutoff = double.NaN, _lastQ = double.NaN, _lastSr = double.NaN;
    private int _lastMode = -1;

    public string Name => "Filter+";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Cutoff", 20.0, 20000.0, () => Cutoff, v => Cutoff = v, "0", "Hz", 3.0),
        new FloatParameter("Resonance", 0.5, 16.0, () => Resonance, v => Resonance = v, "0.0", "Q", 2.0),
        new FloatParameter("Drive", 1.0, 24.0, () => Drive, v => Drive = v, "0.0"),
        new ChoiceParameter("Mode", ModeNames, () => Mode, v => Mode = v),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _bq = new Biquad[_channels];
        _lastMode = -1;
    }

    public IAudioEffect Clone() => new FilterPlusEffect
    {
        Enabled = Enabled, Cutoff = Cutoff, Resonance = Resonance, Drive = Drive, Mode = Mode, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        var bq = _bq;
        if (bq.Length == 0) return;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, bq.Length);
        var mode = Modes[Math.Clamp(Mode, 0, Modes.Length - 1)];

        if (mode != (FilterMode)_lastMode || Cutoff != _lastCutoff || Resonance != _lastQ || _sampleRate != _lastSr)
        {
            _coeffs = BiquadCoefficients.Compute(mode, Cutoff, Resonance, _sampleRate);
            _lastMode = (int)mode;
            _lastCutoff = Cutoff;
            _lastQ = Resonance;
            _lastSr = _sampleRate;
        }

        var coeffs = _coeffs;
        var drive = (float)Math.Max(1e-6, Drive);
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var frames = buffer.Length / channels;

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[i + c];
                var filtered = (float)bq[c].Process(coeffs, dry);
                var shaped = WaveShaper.Shape(filtered, ShaperType.Tanh, drive);
                buffer[i + c] = dry * (1 - mix) + shaped * mix;
            }
        }
    }
}
