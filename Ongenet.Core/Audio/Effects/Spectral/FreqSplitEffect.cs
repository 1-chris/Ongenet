using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects.Spectral;

/// <summary>
/// Frequency crossover split: low and high bands are routed to independent FX chains and summed.
/// Uses Linkwitz-Riley-style biquad crossovers; exposes a spectrum tap for UI analysis.
/// </summary>
public sealed class FreqSplitEffect : SpectralSplitEffectBase, ISpectrumSource
{
    public const string TypeId = "freq_split";

    protected override string GetTypeId() => TypeId;

    public override string Name => "Freq Split";

    public double CrossoverHz { get; set; } = 1000.0;

    private BiquadCoefficients _lp = BiquadCoefficients.Identity;
    private BiquadCoefficients _hp = BiquadCoefficients.Identity;
    private Biquad[] _lpState = Array.Empty<Biquad>();
    private Biquad[] _hpState = Array.Empty<Biquad>();
    private readonly SpectrumScope _scope = new();
    private float[] _scopeScratch = Array.Empty<float>();
    private IReadOnlyList<Parameter>? _parameters;

    public int SampleRate => Format.SampleRate > 0 ? Format.SampleRate : 44100;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Crossover", 80, 8000, () => CrossoverHz, v => CrossoverHz = v, "0", "Hz", 2.0)
    };

    protected override void OnPrepare(AudioFormat format)
    {
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var sr = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _lpState = new Biquad[channels];
        _hpState = new Biquad[channels];
        UpdateCrossover(sr);
    }

    public override void Process(Span<float> buffer)
    {
        var channels = Format.Channels < 1 ? 1 : Format.Channels;
        var sr = Format.SampleRate > 0 ? Format.SampleRate : 44100.0;
        UpdateCrossover(sr);
        _scope.Tap(buffer, channels);

        // Periodic FFT analysis (shared with SpectralAnalyzer) for band metering hooks.
        if (_scopeScratch.Length < 2048) _scopeScratch = new float[2048];
        if (_scope.CaptureLatest(_scopeScratch) >= 64)
            _ = SpectralFftHelper.ComputeMagnitudes(_scopeScratch.AsSpan(0, 2048));

        ProcessDualBand(buffer, (c, idx, sample, low, high) =>
        {
            if ((uint)c >= (uint)_lpState.Length) return;
            var l = (float)_lpState[c].Process(in _lp, sample);
            var h = (float)_hpState[c].Process(in _hp, sample);
            low[idx] = l;
            high[idx] = h;
        });
    }

    public int CaptureLatest(float[] dest) => _scope.CaptureLatest(dest);

    public override IAudioEffect Clone()
    {
        var c = new FreqSplitEffect { CrossoverHz = CrossoverHz };
        CloneBranchesInto(c);
        return c;
    }

    private void UpdateCrossover(double sampleRate)
    {
        var hz = Math.Clamp(CrossoverHz, 80, sampleRate * 0.45);
        _lp = BiquadCoefficients.Compute(FilterMode.LowPass, hz, 0.707, sampleRate);
        _hp = BiquadCoefficients.Compute(FilterMode.HighPass, hz, 0.707, sampleRate);
    }
}
