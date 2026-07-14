using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Five-band peaking EQ with fixed centre frequencies and per-band gain.
/// </summary>
public sealed class Eq5Effect : IAudioEffect
{
    public const string TypeId = "eq5";

    string IAudioEffect.TypeId => TypeId;

    private static readonly double[] Centres = { 60.0, 250.0, 1000.0, 4000.0, 12000.0 };

    public bool Enabled { get; set; } = true;

    public double Band1Db { get; set; }
    public double Band2Db { get; set; }
    public double Band3Db { get; set; }
    public double Band4Db { get; set; }
    public double Band5Db { get; set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private Biquad[,] _bq = new Biquad[0, 0];
    private readonly BiquadCoefficients[] _coeffs = new BiquadCoefficients[5];
    private readonly double[] _lastGains = { double.NaN, double.NaN, double.NaN, double.NaN, double.NaN };
    private double _lastSr = double.NaN;

    public string Name => "EQ 5";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("60 Hz", -18.0, 18.0, () => Band1Db, v => Band1Db = v, "0.#", "dB"),
        new FloatParameter("250 Hz", -18.0, 18.0, () => Band2Db, v => Band2Db = v, "0.#", "dB"),
        new FloatParameter("1 kHz", -18.0, 18.0, () => Band3Db, v => Band3Db = v, "0.#", "dB"),
        new FloatParameter("4 kHz", -18.0, 18.0, () => Band4Db, v => Band4Db = v, "0.#", "dB"),
        new FloatParameter("12 kHz", -18.0, 18.0, () => Band5Db, v => Band5Db = v, "0.#", "dB")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _bq = new Biquad[_channels, 5];
        for (var i = 0; i < 5; i++) _lastGains[i] = double.NaN;
    }

    public IAudioEffect Clone() => new Eq5Effect
    {
        Enabled = Enabled,
        Band1Db = Band1Db, Band2Db = Band2Db, Band3Db = Band3Db,
        Band4Db = Band4Db, Band5Db = Band5Db
    };

    public void Process(Span<float> buffer)
    {
        if (_bq.GetLength(0) == 0) return;
        var channels = Math.Min(_channels < 1 ? 1 : _channels, _bq.GetLength(0));
        Span<double> gains = stackalloc double[5]
        {
            Band1Db, Band2Db, Band3Db, Band4Db, Band5Db
        };

        var dirty = _sampleRate != _lastSr;
        for (var b = 0; b < 5 && !dirty; b++)
            if (gains[b] != _lastGains[b]) dirty = true;

        if (dirty)
        {
            for (var b = 0; b < 5; b++)
            {
                _coeffs[b] = BiquadCoefficients.ComputeEq(EqBandType.Bell, Centres[b], 1.0, gains[b], _sampleRate);
                _lastGains[b] = gains[b];
            }

            _lastSr = _sampleRate;
        }

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                double s = buffer[i + c];
                for (var b = 0; b < 5; b++) s = _bq[c, b].Process(_coeffs[b], s);
                buffer[i + c] = (float)s;
            }
        }
    }
}
