using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects.Spectral;

/// <summary>
/// Transient split: fast and slow envelope followers isolate attack transients vs sustain body;
/// each component is routed to an independent FX chain before summing.
/// </summary>
public sealed class TransientSplitEffect : SpectralSplitEffectBase
{
    public const string TypeId = "transient_split";

    protected override string GetTypeId() => TypeId;

    public override string Name => "Transient Split";

    public double AttackMs { get; set; } = 0.1;
    public double SustainMs { get; set; } = 40.0;

    private readonly EnvelopeFollower _fast = new();
    private readonly EnvelopeFollower _slow = new();
    private double _sampleRate = 44100.0;
    private IReadOnlyList<Parameter>? _parameters;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Attack", 0.05, 5, () => AttackMs, v => AttackMs = v, "0.##", "ms"),
        new FloatParameter("Sustain", 5, 200, () => SustainMs, v => SustainMs = v, "0", "ms")
    };

    protected override void OnPrepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _fast.Reset();
        _slow.Reset();
        UpdateFollowers();
    }

    public override void Process(Span<float> buffer)
    {
        UpdateFollowers();

        ProcessDualBand(buffer, (_, idx, sample, sustainPath, transientPath) =>
        {
            var peak = sample < 0 ? -sample : sample;
            var fast = _fast.Process(peak);
            var slow = _slow.Process(peak);
            var transientEnv = Math.Max(0.0, fast - slow);
            var bodyEnv = slow;
            var sum = transientEnv + bodyEnv + 1e-9;
            transientPath[idx] = sample * (float)(transientEnv / sum);
            sustainPath[idx] = sample * (float)(bodyEnv / sum);
        });
    }

    public override IAudioEffect Clone()
    {
        var c = new TransientSplitEffect { AttackMs = AttackMs, SustainMs = SustainMs };
        CloneBranchesInto(c);
        return c;
    }

    private void UpdateFollowers()
    {
        _fast.SetTimes(AttackMs, Math.Max(AttackMs * 4, 5), _sampleRate);
        _slow.SetTimes(SustainMs * 0.25, SustainMs, _sampleRate);
    }
}
