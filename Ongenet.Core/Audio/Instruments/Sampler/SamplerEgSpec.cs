using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// Captured DAHDSR envelope settings (delay/attack/hold/decay/sustain/release), used to configure a fresh
/// <see cref="DahdsrEnvelope"/> per voice. Format-neutral: filled by the SFZ loader from <c>ampeg_</c>/
/// <c>fileg_</c>/<c>pitcheg_</c> opcodes and by the SF2 loader from volume/modulation-envelope generators.
/// </summary>
public readonly struct SamplerEgSpec
{
    public double Delay { get; init; }
    public double Attack { get; init; }
    public double Hold { get; init; }
    public double Decay { get; init; }
    public double Sustain { get; init; } // 0..1
    public double Release { get; init; }

    /// <summary>Applies these settings to an envelope.</summary>
    public void ApplyTo(DahdsrEnvelope env)
    {
        env.DelaySeconds = Delay;
        env.AttackSeconds = Attack;
        env.HoldSeconds = Hold;
        env.DecaySeconds = Decay;
        env.SustainLevel = Sustain;
        env.ReleaseSeconds = Release < 0.001 ? 0.001 : Release;
    }
}
