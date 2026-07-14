using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A parametric equaliser: a chain of <see cref="EqBand"/> biquads applied in series. The band
/// list is UI-editable; <see cref="CommitBands"/> publishes a lock-free snapshot the audio thread
/// reads. Exposes its post-EQ output for the interactive spectrum display.
/// </summary>
public sealed class EqEffect : IAudioEffect, ISpectrumSource, IProjectStatefulComponent
{
    public const string TypeId = "eq";

    string IAudioEffect.TypeId => TypeId;

    private readonly List<EqBand> _bands = new();
    private volatile EqBand[] _active = Array.Empty<EqBand>();

    private int _channels = 2;
    private double _sampleRate = 44100.0;

    private readonly SpectrumScope _scope = new();

    public string Name => "EQ";
    public bool Enabled { get; set; } = true;
    public int SampleRate => (int)_sampleRate;

    public bool MorphMode { get; set; }
    public double Morph { get; set; }

    /// <summary>The editable bands. Mutate, then call <see cref="CommitBands"/>.</summary>
    public IReadOnlyList<EqBand> Bands => _bands;

    private static readonly double[] MorphFreqs = { 80, 250, 1000, 4000, 12000 };
    private static readonly double[][] MorphNodeGains =
    {
        new[] { 6.0, 2.0, 0.0, -1.0, -2.0 },
        new[] { 2.0, 4.0, 0.0, 0.0, 0.0 },
        new[] { 0.0, 0.0, 0.0, 0.0, 0.0 },
        new[] { 0.0, 0.0, 2.0, 4.0, 2.0 },
        new[] { -2.0, 0.0, 1.0, 3.0, 6.0 },
    };

    private readonly EqBand[] _morphBands = new EqBand[5];
    private volatile EqBand[] _morphActive = Array.Empty<EqBand>();
    private double _lastMorph = double.NaN;

    // EQ generic knobs when not using the interactive graph exclusively.
    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new BoolParameter("Morph Mode", () => MorphMode, v => MorphMode = v),
        new FloatParameter("Morph", 0.0, 1.0, () => Morph, v => Morph = v)
    };

    private IReadOnlyList<Parameter>? _parameters;

    public EqEffect()
    {
        // Sensible flat starting point: three draggable points across the spectrum.
        _bands.Add(new EqBand(EqBandType.LowShelf, 80.0, 0.0, 0.7));
        _bands.Add(new EqBand(EqBandType.Bell, 1000.0, 0.0, 1.0));
        _bands.Add(new EqBand(EqBandType.HighShelf, 8000.0, 0.0, 0.7));
        _active = _bands.ToArray();

        for (var i = 0; i < _morphBands.Length; i++)
            _morphBands[i] = new EqBand(EqBandType.Bell, MorphFreqs[i], 0.0, 1.0);
        CommitMorphBands();
    }

    public void AddBand(EqBand band)
    {
        _bands.Add(band);
        CommitBands();
    }

    public void RemoveBand(EqBand band)
    {
        if (_bands.Remove(band)) CommitBands();
    }

    /// <summary>Prepares all bands and publishes the snapshot the audio thread reads.</summary>
    public void CommitBands()
    {
        foreach (var band in _bands) band.Prepare(_channels);
        _active = _bands.ToArray();
    }

    private void CommitMorphBands()
    {
        var t = Math.Clamp(Morph, 0, 1) * (MorphNodeGains.Length - 1);
        var i0 = (int)Math.Floor(t);
        var i1 = Math.Min(i0 + 1, MorphNodeGains.Length - 1);
        var frac = t - i0;

        for (var b = 0; b < _morphBands.Length; b++)
        {
            var gain = MorphNodeGains[i0][b] * (1 - frac) + MorphNodeGains[i1][b] * frac;
            _morphBands[b].Frequency = MorphFreqs[b];
            _morphBands[b].GainDb = gain;
            _morphBands[b].Prepare(_channels);
        }

        _morphActive = _morphBands;
        _lastMorph = Morph;
    }

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        CommitBands();
    }

    public IAudioEffect Clone()
    {
        var copy = new EqEffect { Enabled = Enabled, MorphMode = MorphMode, Morph = Morph };
        copy._bands.Clear();
        foreach (var band in _bands) copy._bands.Add(band.Clone());
        copy._active = copy._bands.ToArray();
        copy.CommitMorphBands();
        return copy;
    }

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        if (MorphMode)
        {
            if (Morph != _lastMorph) CommitMorphBands();
        }

        var bands = MorphMode ? _morphActive : _active;
        var sr = _sampleRate;
        foreach (var band in bands) band.EnsureCoeffs(sr);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var s = buffer[i + c];
                foreach (var band in bands) s = band.Process(c, s);
                buffer[i + c] = s;
            }
        }

        _scope.Tap(buffer, channels);
    }

    public int CaptureLatest(float[] dest) => _scope.CaptureLatest(dest);

    // --- IProjectStatefulComponent: the band list (the EQ has no generic parameters) ---

    public void WriteProjectState(OngenWriter writer)
    {
        writer.WriteBool(MorphMode);
        writer.WriteDouble(Morph);
        writer.WriteInt(_bands.Count);
        foreach (var band in _bands)
        {
            writer.WriteInt((int)band.Type);
            writer.WriteDouble(band.Frequency);
            writer.WriteDouble(band.GainDb);
            writer.WriteDouble(band.Q);
        }
    }

    public void ReadProjectState(OngenReader reader)
    {
        MorphMode = reader.ReadBool();
        Morph = reader.ReadDouble();
        var count = reader.ReadInt();
        _bands.Clear();
        for (var i = 0; i < count; i++)
        {
            var type = (EqBandType)reader.ReadInt();
            var freq = reader.ReadDouble();
            var gain = reader.ReadDouble();
            var q = reader.ReadDouble();
            _bands.Add(new EqBand(type, freq, gain, q));
        }

        CommitBands();
        CommitMorphBands();
    }
}
