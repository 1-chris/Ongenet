using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects.Spectral;

/// <summary>
/// Harmonic/percussive split (HPSS-lite): a low-pass tonal estimate is subtracted from the dry signal
/// to isolate transient/percussive content. Each path feeds an independent FX chain.
/// </summary>
public sealed class HarmonicSplitEffect : SpectralSplitEffectBase
{
    public const string TypeId = "harmonic_split";

    protected override string GetTypeId() => TypeId;

    public override string Name => "Harmonic Split";

    /// <summary>Cutoff (Hz) for the tonal/harmonic estimate.</summary>
    public double CutoffHz { get; set; } = 800.0;

    /// <summary>Softness of the harmonic/percussive boundary (0 = hard, 1 = soft).</summary>
    public double Softness { get; set; } = 0.35;

    private BiquadCoefficients _lp = BiquadCoefficients.Identity;
    private Biquad[] _lpState = Array.Empty<Biquad>();
    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Cutoff", 120, 4000, () => CutoffHz, v => CutoffHz = v, "0", "Hz", 2.0),
        new FloatParameter("Softness", 0, 1, () => Softness, v => Softness = v)
    };

    protected override void OnPrepare(AudioFormat format)
    {
        var channels = format.Channels < 1 ? 1 : format.Channels;
        _lpState = new Biquad[channels];
        UpdateFilter(format.SampleRate > 0 ? format.SampleRate : 44100.0);
    }

    public override void Process(Span<float> buffer)
    {
        var sr = Format.SampleRate > 0 ? Format.SampleRate : 44100.0;
        UpdateFilter(sr);
        var soft = (float)Math.Clamp(Softness, 0, 1);

        ProcessDualBand(buffer, (c, idx, sample, harmonic, percussive) =>
        {
            if ((uint)c >= (uint)_lpState.Length)
            {
                harmonic[idx] = sample;
                return;
            }

            var tonal = (float)_lpState[c].Process(in _lp, sample);
            var transient = sample - tonal;
            // Blend toward a softer HPSS boundary when Softness is raised.
            harmonic[idx] = tonal * (1f - soft * 0.5f) + sample * soft * 0.25f;
            percussive[idx] = transient * (1f - soft * 0.25f);
        });
    }

    public override IAudioEffect Clone()
    {
        var c = new HarmonicSplitEffect { CutoffHz = CutoffHz, Softness = Softness };
        CloneBranchesInto(c);
        return c;
    }

    private void UpdateFilter(double sampleRate)
    {
        var hz = Math.Clamp(CutoffHz, 120, sampleRate * 0.45);
        _lp = BiquadCoefficients.Compute(FilterMode.LowPass, hz, 0.707, sampleRate);
    }
}
