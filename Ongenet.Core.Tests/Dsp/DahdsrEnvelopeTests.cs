using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Tests.Dsp;

public class DahdsrEnvelopeTests
{
    private static DahdsrEnvelope Make(double delay = 0, double attack = 0, double hold = 0,
        double decay = 0, double sustain = 1, double release = 0.005)
    {
        var env = new DahdsrEnvelope
        {
            DelaySeconds = delay, AttackSeconds = attack, HoldSeconds = hold,
            DecaySeconds = decay, SustainLevel = sustain, ReleaseSeconds = release
        };
        env.SetSampleRate(1000); // 1 sample = 1 ms, convenient for assertions
        return env;
    }

    [Fact]
    public void DelayHoldsAtZeroBeforeAttack()
    {
        var env = Make(delay: 0.010, attack: 0.0);
        env.Gate();
        for (var i = 0; i < 10; i++) Assert.Equal(0f, env.Process()); // 10 ms of delay
        Assert.True(env.Process() > 0f); // attack begins
    }

    [Fact]
    public void ReachesAndHoldsSustain()
    {
        var env = Make(attack: 0.005, decay: 0.005, sustain: 0.5);
        env.Gate();
        for (var i = 0; i < 100; i++) env.Process();
        Assert.Equal(0.5f, env.Process(), 0.01f);
    }

    [Fact]
    public void ReleaseFallsToZeroAndDeactivates()
    {
        var env = Make(attack: 0.0, sustain: 1.0, release: 0.010);
        env.Gate();
        env.Process();
        env.Release();
        Assert.True(env.IsReleasing);
        for (var i = 0; i < 50; i++) env.Process();
        Assert.False(env.IsActive);
        Assert.Equal(0f, env.Process());
    }

    [Fact]
    public void HoldKeepsPeakBeforeDecay()
    {
        var env = Make(attack: 0.0, hold: 0.010, decay: 0.010, sustain: 0.0);
        env.Gate();
        env.Process(); // attack completes immediately
        for (var i = 0; i < 9; i++) Assert.Equal(1f, env.Process(), 0.001f); // held at peak
    }
}
