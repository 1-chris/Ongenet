using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// Captured DAHDSR settings for an SFZ envelope group (<c>ampeg_</c>/<c>fileg_</c>/<c>pitcheg_</c>),
/// used to configure a fresh <see cref="DahdsrEnvelope"/> per voice.
/// </summary>
public readonly struct SfzEgSpec
{
    public double Delay { get; init; }
    public double Attack { get; init; }
    public double Hold { get; init; }
    public double Decay { get; init; }
    public double Sustain { get; init; } // 0..1
    public double Release { get; init; }

    /// <summary>Reads an <c>{prefix}_delay/attack/hold/decay/sustain/release</c> envelope from opcodes.</summary>
    public static SfzEgSpec Read(SfzOpcodes o, string prefix, double defaultSustain) => new()
    {
        Delay = o.GetDouble(prefix + "_delay", 0.0),
        Attack = o.GetDouble(prefix + "_attack", 0.0),
        Hold = o.GetDouble(prefix + "_hold", 0.0),
        Decay = o.GetDouble(prefix + "_decay", 0.0),
        Sustain = AudioMath.Clamp(o.GetDouble(prefix + "_sustain", defaultSustain) / 100.0, 0.0, 1.0),
        Release = o.GetDouble(prefix + "_release", 0.0)
    };

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
