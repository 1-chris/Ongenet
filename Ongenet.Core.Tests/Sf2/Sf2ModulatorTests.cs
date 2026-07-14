using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Audio.Instruments.Sampler.Sf2;
using Xunit;

namespace Ongenet.Core.Tests.Sf2;

public class Sf2ModulatorTests
{
    [Fact]
    public void Default_velocity_modulator_sets_amp_veltrack()
    {
        var (vel, routes) = Sf2Modulator.ApplyToRoutes(
            new[] { Sf2Modulator.DefaultVelToAtten }, 0);
        Assert.InRange(vel, 90, 110);
        Assert.Empty(routes);
    }

    [Fact]
    public void Cc1_vib_pitch_becomes_route()
    {
        var (_, routes) = Sf2Modulator.ApplyToRoutes(
            new[] { Sf2Modulator.DefaultCc1ToVibPitch }, 100);
        Assert.Contains(routes, r =>
            r.Target == SamplerModTarget.PitchCents && r.Source == SamplerModSource.Cc && r.SourceIndex == 1);
    }

    [Fact]
    public void ReadMods_parses_10_byte_records()
    {
        var bytes = new byte[10];
        BitConverter.GetBytes((ushort)(0x80 | 7)).CopyTo(bytes, 0); // CC7
        BitConverter.GetBytes((ushort)Sf2Gen.InitialAttenuation).CopyTo(bytes, 2);
        BitConverter.GetBytes((short)(-200)).CopyTo(bytes, 4);
        var mods = Sf2Modulator.ReadMods(bytes, 0, 10);
        Assert.Single(mods);
        Assert.True(Sf2ModSources.IsCc(mods[0].SrcOper));
        Assert.Equal(7, Sf2ModSources.CcIndex(mods[0].SrcOper));
    }
}
