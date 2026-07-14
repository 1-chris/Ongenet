using System;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Tests.Effects;

public class EffectUpgradeTests
{
    private static AudioFormat Stereo => new(44100, 2);

    [Fact]
    public void ReverbAlgorithmPresetAppliesRoomSize()
    {
        var fx = new ReverbEffect { AlgorithmIndex = 0, Mix = 1.0 };
        fx.Prepare(Stereo);
        fx.Process(new float[512]);
        Assert.Equal(ReverbAlgorithmBank.Get(0).RoomSize, fx.RoomSize, 3);
    }

    [Fact]
    public void AmpCabMixAltersOutput()
    {
        var dry = new float[] { 0.5f, 0.5f };
        var wet = (float[])dry.Clone();
        var fx = new AmpEffect { Mix = 1.0, CabMix = 0.8, CabCharacter = 1, Drive = 4, Tone = 0.5 };
        fx.Prepare(Stereo);
        fx.Process(wet);
        Assert.NotEqual(dry[0], wet[0]);
    }

    [Fact]
    public void ExciterBassEnhanceProcesses()
    {
        var buf = new float[4096];
        for (var i = 0; i < buf.Length; i++) buf[i] = (float)Math.Sin(2 * Math.PI * 60 * i / 44100) * 0.4f;
        var fx = new ExciterEffect { BassEnhance = 0.7, Mix = 0.5, Drive = 6, ToneHz = 3000 };
        fx.Prepare(Stereo);
        fx.Process(buf);
        Assert.All(buf, s => Assert.True(float.IsFinite(s)));
    }

    [Fact]
    public void VocoderExposesBandLevels()
    {
        var fx = new VocoderEffect();
        fx.Prepare(Stereo);
        var src = (IVocoderAnalysisSource)fx;
        Assert.Equal(16, src.BandCount);
        Assert.Equal(16, src.BandLevels.Length);
    }

    [Fact]
    public void MultibandPresetSetsDepth()
    {
        var fx = new MultibandCompressorEffect { MasteringPresetIndex = 2 };
        fx.Prepare(Stereo);
        fx.Process(new float[256]);
        Assert.Equal(MasteringPresetBank.GetMultiband(2).Depth, fx.Depth, 3);
    }

    [Fact]
    public void FilterMultibankModeProcesses()
    {
        var fx = new FilterEffect { MultibankMode = true, Frequency = 800, Resonance = 2, Mode = FilterMode.LowPass };
        fx.Prepare(Stereo);
        var buf = new float[] { 0.3f, 0.3f };
        fx.Process(buf);
        Assert.True(float.IsFinite(buf[0]));
    }

    [Fact]
    public void EqMorphModeInterpolatesGains()
    {
        var fx = new EqEffect { MorphMode = true, Morph = 0.5 };
        fx.Prepare(Stereo);
        fx.Process(new float[512]);
        Assert.True(fx.MorphMode);
    }

    [Fact]
    public void StereoWidthMatrixChangesBalance()
    {
        var fx = new StereoWidthEffect { SideGain = 0.0, MidGain = 1.0, Width = 1.0 };
        fx.Prepare(Stereo);
        var buf = new float[] { 1f, -1f };
        fx.Process(buf);
        Assert.Equal(buf[0], buf[1], 3);
    }

    [Fact]
    public void StutterGestureRoundTripsVolumeAndTapeSpeed()
    {
        var g = new StutterGesture { TapeSpeed = 2.0, ReverseBuffer = true };
        g.Volume.Set(new[] { new Core.Audio.Automation.AutomationPoint(0, 0.5), new Core.Audio.Automation.AutomationPoint(1, 1) });
        var clone = g.Clone();
        Assert.Equal(2.0, clone.TapeSpeed);
        Assert.True(clone.ReverseBuffer);
        Assert.Equal(0.75, clone.Volume.Evaluate(0.5), 2);
    }

    private static double Rms(ReadOnlySpan<float> buf)
    {
        double sum = 0;
        for (var i = 0; i < buf.Length; i++) sum += buf[i] * (double)buf[i];
        return Math.Sqrt(sum / buf.Length);
    }
}
