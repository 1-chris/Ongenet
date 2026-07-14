using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>Peak limiter with spectral threshold analysis and gain-reduction metering.</summary>
public sealed class PeakLimiterEffect : IAudioEffect, IGainReductionSource
{
    public const string TypeId = "peak_limiter";

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Peak Limiter";
    public bool Enabled { get; set; } = true;

    public double ThresholdDb { get; set; } = 0.0;
    public double CeilingDb { get; set; } = -0.3;
    public double ReleaseMs { get; set; } = 80.0;
    public bool SpectralLimiter { get; set; }
    public int MasteringPresetIndex { get; set; }

    public double GainReductionDb { get; private set; }

    private int _channels = 2;
    private double _sampleRate = 44100;
    private readonly EnvelopeFollower _follower = new();
    private readonly EnvelopeFollower _spectralFollower = new();
    private int _lastPresetIndex = -1;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Preset",
            Array.ConvertAll(MasteringPresetBank.LimiterPresets, p => p.Name),
            () => MasteringPresetIndex, v => MasteringPresetIndex = v),
        new FloatParameter("Threshold", -24, 0, () => ThresholdDb, v => ThresholdDb = v, "0.#", "dB"),
        new FloatParameter("Ceiling", -24, 0, () => CeilingDb, v => CeilingDb = v, "0.#", "dB"),
        new FloatParameter("Release", 1, 500, () => ReleaseMs, v => ReleaseMs = v, "0", "ms", 2.0),
        new BoolParameter("Spectral", () => SpectralLimiter, v => SpectralLimiter = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _follower.Reset();
        _spectralFollower.Reset();
    }

    public IAudioEffect Clone() => new PeakLimiterEffect
    {
        Enabled = Enabled, ThresholdDb = ThresholdDb, CeilingDb = CeilingDb,
        ReleaseMs = ReleaseMs, SpectralLimiter = SpectralLimiter, MasteringPresetIndex = MasteringPresetIndex
    };

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        ApplyPresetIfChanged();
        var ch = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / ch;
        _follower.SetTimes(0.5, ReleaseMs, _sampleRate);
        _spectralFollower.SetTimes(2.0, ReleaseMs * 2.5, _sampleRate);
        var inGain = (float)AudioMath.Db2Lin(-ThresholdDb);
        var ceiling = (float)AudioMath.Db2Lin(CeilingDb);
        var grPeak = 0.0;

        for (var f = 0; f < frames; f++)
        {
            float peak = 0;
            for (var c = 0; c < ch; c++)
            {
                var a = MathF.Abs(buffer[f * ch + c] * inGain);
                if (a > peak) peak = a;
            }

            if (SpectralLimiter)
            {
                var slow = (float)_spectralFollower.Process(peak);
                if (slow > peak) peak = slow;
            }

            var env = (float)_follower.Process(peak);
            var grDb = env > 1e-9 ? 20.0 * Math.Log10(ceiling / env) : 0.0;
            if (grDb < grPeak) grPeak = grDb;
            var gain = env > 1e-9f ? ceiling / env : 1f;

            for (var c = 0; c < ch; c++)
            {
                var i = f * ch + c;
                var x = buffer[i] * inGain * gain;
                buffer[i] = Math.Clamp(x, -ceiling, ceiling);
            }
        }

        GainReductionDb = grPeak;
    }

    private void ApplyPresetIfChanged()
    {
        if (MasteringPresetIndex == _lastPresetIndex) return;
        _lastPresetIndex = MasteringPresetIndex;
        var preset = MasteringPresetBank.GetLimiter(MasteringPresetIndex);
        ThresholdDb = preset.ThresholdDb;
        CeilingDb = preset.CeilingDb;
        ReleaseMs = preset.ReleaseMs;
        SpectralLimiter = preset.Spectral;
    }
}
