using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// Captured DAHDSR envelope settings (delay/start/attack/hold/decay/sustain/release).
/// </summary>
public readonly record struct SamplerEgSpec
{
    public double Delay { get; init; }
    public double Start { get; init; }
    public double Attack { get; init; }
    public double Hold { get; init; }
    public double Decay { get; init; }
    public double Sustain { get; init; } // 0..1
    public double Release { get; init; }

    /// <summary>Velocity→stage offsets (seconds or level), applied at note-on.</summary>
    public double Vel2Delay { get; init; }
    public double Vel2Attack { get; init; }
    public double Vel2Hold { get; init; }
    public double Vel2Decay { get; init; }
    public double Vel2Sustain { get; init; }
    public double Vel2Release { get; init; }

    public void ApplyTo(DahdsrEnvelope env, double velocityNorm = 0)
    {
        env.DelaySeconds = Delay + Vel2Delay * velocityNorm;
        env.StartLevel = Start;
        env.AttackSeconds = System.Math.Max(0, Attack + Vel2Attack * velocityNorm);
        env.HoldSeconds = System.Math.Max(0, Hold + Vel2Hold * velocityNorm);
        env.DecaySeconds = System.Math.Max(0, Decay + Vel2Decay * velocityNorm);
        env.SustainLevel = AudioMath.Clamp(Sustain + Vel2Sustain * velocityNorm, 0, 1);
        var rel = Release + Vel2Release * velocityNorm;
        env.ReleaseSeconds = rel < 0.001 ? 0.001 : rel;
    }

    /// <summary>Applies with velocity 0 (backward-compatible).</summary>
    public void ApplyTo(DahdsrEnvelope env) => ApplyTo(env, 0);
}
