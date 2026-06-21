using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Audio.Instruments.Sampler.Sf2;
using Xunit;

namespace Ongenet.Core.Tests.Sf2;

/// <summary>
/// Unit tests for the SF2 generator layer: the unit conversions (timecents/centibels/absolute-cents) and
/// the generator-accumulation rules (preset offsets add, ranges intersect, instrument-only generators are
/// not contributed by the preset, root key falls back to the sample) — exercised through the public
/// <see cref="Sf2Convert"/> helpers and <see cref="Sf2Loader.BuildRegion"/>.
/// </summary>
public class Sf2GeneratorTests
{
    [Fact]
    public void TimecentsToSeconds_Doubles_PerOctave()
    {
        Assert.Equal(1.0, Sf2Convert.TimecentsToSeconds(0), 6);
        Assert.Equal(2.0, Sf2Convert.TimecentsToSeconds(1200), 6);
        Assert.Equal(0.5, Sf2Convert.TimecentsToSeconds(-1200), 6);
    }

    [Fact]
    public void AbsoluteCents6900_IsA440()
        => Assert.Equal(440.0, Sf2Convert.AbsoluteCentsToHz(6900), 0);

    [Fact]
    public void AttenuationAndSustain_CentibelsToLinear()
    {
        Assert.Equal(1.0, Sf2Convert.AttenuationToGain(0), 6);
        Assert.Equal(0.1, Sf2Convert.AttenuationToGain(200), 3);   // 20 dB down
        Assert.Equal(1.0, Sf2Convert.SustainCentibelsToLevel(0), 6);
        Assert.Equal(0.5, Sf2Convert.SustainCentibelsToLevel(60), 2); // ~ -3 dB
    }

    private static Sf2GenItem Scalar(short v) => new(Sf2Gen.EndOper, (ushort)v);
    private static Sf2GenItem RangeItem(int lo, int hi) => new(Sf2Gen.EndOper, (ushort)(lo | (hi << 8)));

    private static SamplerSample DummySample(int frames = 100)
        => SamplerSample.FromResident(new AudioSampleBuffer(new float[frames], 1, 44100));

    private static Sf2SampleHeader DummyHeader(int originalPitch = 69)
        => new("s", 0, 100, 10, 90, 44100, originalPitch, 0, 0, 1);

    [Fact]
    public void KeyAndVelRanges_IntersectPresetIntoInstrument()
    {
        var inst = new Dictionary<Sf2Gen, Sf2GenItem> { [Sf2Gen.KeyRange] = RangeItem(0, 127), [Sf2Gen.VelRange] = RangeItem(0, 127) };
        var preset = new Dictionary<Sf2Gen, Sf2GenItem> { [Sf2Gen.KeyRange] = RangeItem(60, 72), [Sf2Gen.VelRange] = RangeItem(64, 127) };

        var r = Sf2Loader.BuildRegion(inst, preset, DummyHeader(), DummySample(), 0);

        Assert.Equal(60, r.LoKey);
        Assert.Equal(72, r.HiKey);
        Assert.Equal(64, r.LoVel);
        Assert.Equal(127, r.HiVel);
    }

    [Fact]
    public void CoarseTune_PresetOffsetIsAdditive()
    {
        var inst = new Dictionary<Sf2Gen, Sf2GenItem> { [Sf2Gen.CoarseTune] = Scalar(12) };
        var preset = new Dictionary<Sf2Gen, Sf2GenItem> { [Sf2Gen.CoarseTune] = Scalar(2) };

        var r = Sf2Loader.BuildRegion(inst, preset, DummyHeader(), DummySample(), 0);

        Assert.Equal(14.0, r.TransposeSemis, 6);
    }

    [Fact]
    public void InstrumentOnlyGenerator_IgnoresPresetContribution()
    {
        // sampleModes is instrument-only: the preset's value (3 = loop+sustain) must NOT override the
        // instrument's (1 = loop continuous).
        var inst = new Dictionary<Sf2Gen, Sf2GenItem> { [Sf2Gen.SampleModes] = Scalar(1) };
        var preset = new Dictionary<Sf2Gen, Sf2GenItem> { [Sf2Gen.SampleModes] = Scalar(3) };

        var r = Sf2Loader.BuildRegion(inst, preset, DummyHeader(), DummySample(), 0);

        Assert.Equal(SamplerLoopMode.LoopContinuous, r.LoopMode);
    }

    [Fact]
    public void RootKey_FallsBackToSampleOriginalPitch()
    {
        var r = Sf2Loader.BuildRegion(new Dictionary<Sf2Gen, Sf2GenItem>(),
            new Dictionary<Sf2Gen, Sf2GenItem>(), DummyHeader(originalPitch: 64), DummySample(), 0);

        Assert.Equal(64, r.PitchKeycenter);
    }

    [Fact]
    public void OverridingRootKey_WinsOverSample()
    {
        var inst = new Dictionary<Sf2Gen, Sf2GenItem> { [Sf2Gen.OverridingRootKey] = Scalar(48) };
        var r = Sf2Loader.BuildRegion(inst, new Dictionary<Sf2Gen, Sf2GenItem>(), DummyHeader(69), DummySample(), 0);

        Assert.Equal(48, r.PitchKeycenter);
    }

    [Fact]
    public void InitialAttenuation_PresetOffsetReducesGain()
    {
        var preset = new Dictionary<Sf2Gen, Sf2GenItem> { [Sf2Gen.InitialAttenuation] = Scalar(200) }; // -20 dB
        var r = Sf2Loader.BuildRegion(new Dictionary<Sf2Gen, Sf2GenItem>(), preset, DummyHeader(), DummySample(), 0);

        Assert.Equal(0.1, r.Gain, 3);
    }

    [Fact]
    public void LoopPoints_AreSampleRelative()
    {
        var r = Sf2Loader.BuildRegion(new Dictionary<Sf2Gen, Sf2GenItem>(),
            new Dictionary<Sf2Gen, Sf2GenItem>(), DummyHeader(), DummySample(100), 0);

        Assert.Equal(0, r.Offset);
        Assert.Equal(100, r.End);
        Assert.Equal(10, r.LoopStart);   // StartLoop(10) - Start(0)
        Assert.Equal(90, r.LoopEnd);     // EndLoop(90) - Start(0)
    }
}
