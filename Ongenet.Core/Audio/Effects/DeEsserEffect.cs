using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// De-esser: band-pass detector around Frequency drives an EnvelopeFollower that ducks the
/// full-band signal when the sibilant band exceeds Threshold, scaled by Amount.
/// </summary>
public sealed class DeEsserEffect : IAudioEffect
{
    public const string TypeId = "deesser";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Frequency { get; set; } = 6000.0;
    public double Threshold { get; set; } = -24.0;
    public double Amount { get; set; } = 0.7;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[] _bp = Array.Empty<Biquad>();
    private BiquadCoefficients _coeffs = BiquadCoefficients.Identity;
    private double _lastFreq = double.NaN, _lastSr = double.NaN;
    private readonly EnvelopeFollower _follower = new();

    public string Name => "De-Esser";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Frequency", 2000.0, 12000.0, () => Frequency, v => Frequency = v, "0", "Hz", 2.0),
        new FloatParameter("Threshold", -60.0, 0.0, () => Threshold, v => Threshold = v, "0.#", "dB"),
        new FloatParameter("Amount", 0.0, 1.0, () => Amount, v => Amount = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _bp = new Biquad[_channels];
        _follower.Reset();
        _lastFreq = double.NaN;
    }

    public IAudioEffect Clone() => new DeEsserEffect
    {
        Enabled = Enabled, Frequency = Frequency, Threshold = Threshold, Amount = Amount
    };

    public void Process(Span<float> buffer)
    {
        var channels = Math.Min(_channels < 1 ? 1 : _channels, _bp.Length);
        if (channels <= 0) return;

        if (Frequency != _lastFreq || _sampleRate != _lastSr)
        {
            _coeffs = BiquadCoefficients.Compute(FilterMode.BandPass, Frequency, 2.0, _sampleRate);
            _lastFreq = Frequency;
            _lastSr = _sampleRate;
        }

        _follower.SetTimes(1.0, 40.0, _sampleRate);
        var coeffs = _coeffs;
        var bp = _bp;
        var amount = Math.Clamp(Amount, 0, 1);
        var threshold = Threshold;

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            float detect = 0;
            for (var c = 0; c < channels; c++)
            {
                var band = (float)bp[c].Process(coeffs, buffer[i + c]);
                var a = band < 0 ? -band : band;
                if (a > detect) detect = a;
            }

            var env = _follower.Process(detect);
            var over = AudioMath.Lin2Db(env) - threshold;
            var grDb = over > 0 ? over * amount : 0;
            var gain = (float)AudioMath.Db2Lin(-grDb);

            for (var c = 0; c < channels; c++) buffer[i + c] *= gain;
        }
    }
}
