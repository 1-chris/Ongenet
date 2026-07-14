using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Audio.Instruments.Sampler.Sfz;
using Ongenet.Core.Audio.Files;
using Xunit;

namespace Ongenet.Core.Tests.Sfz;

public class SfzCompletenessTests
{
    private static SamplerSample DummySample()
    {
        var buf = new float[44100];
        for (var i = 0; i < buf.Length; i++) buf[i] = 0.2f;
        return SamplerSample.FromResident(new AudioSampleBuffer(buf, 1, 44100));
    }

    private static SamplerRegion Region(string opcodes)
    {
        var doc = SfzParser.Parse("<region> sample=x.wav " + opcodes);
        var sfzRegion = doc.Regions[0];
        // Force sample onto region via builder with dummy
        return SfzRegionBuilder.Build(sfzRegion, DummySample())!;
    }

    [Fact]
    public void Locc_gate_parsed()
    {
        var r = Region("lokey=60 hikey=60 locc64=64 hicc64=127");
        Assert.Single(r.CcGates);
        Assert.Equal(64, r.CcGates[0].Cc);
        Assert.Equal(64, r.CcGates[0].Lo);
        Assert.Equal(127, r.CcGates[0].Hi);
    }

    [Fact]
    public void Lorand_hirand_parsed()
    {
        var r = Region("lokey=60 hikey=60 lorand=0.25 hirand=0.75");
        Assert.Equal(0.25, r.LoRand);
        Assert.Equal(0.75, r.HiRand);
    }

    [Fact]
    public void Volume_oncc_becomes_mod_route()
    {
        var r = Region("lokey=60 hikey=60 volume_oncc7=12");
        Assert.Contains(r.ModRoutes, m =>
            m.Target == SamplerModTarget.AmplitudeDb && m.Source == SamplerModSource.Cc && m.SourceIndex == 7);
    }

    [Fact]
    public void Xfade_key_parsed()
    {
        var r = Region("lokey=0 hikey=127 xfin_lokey=48 xfin_hikey=60 xfout_lokey=72 xfout_hikey=84");
        Assert.NotNull(r.Xfade);
        Assert.True(r.Xfade!.IsActive);
        Assert.Equal(0f, r.Xfade.Evaluate(40, 100, 0));
        Assert.Equal(1f, r.Xfade.Evaluate(60, 100, 0));
    }

    [Fact]
    public void Set_cc_and_note_offset_in_control()
    {
        var doc = SfzParser.Parse("<control> note_offset=12 octave_offset=-1 set_cc1=90\n<region> sample=x.wav key=c4");
        Assert.Equal(12, doc.Control.NoteOffset);
        Assert.Equal(-1, doc.Control.OctaveOffset);
        Assert.Equal(90, doc.Control.InitialCcValues[1]);
    }

    [Fact]
    public void Curve_header_parsed()
    {
        var doc = SfzParser.Parse("<curve> curve_index=3 v0=0 v64=0.5 v127=1\n<region> sample=x.wav key=c4");
        var c = doc.Curves.Get(3);
        Assert.NotNull(c);
        Assert.Equal(0.5f, c!.Values[64]);
    }

    [Fact]
    public void Sw_down_not_aliased_to_sw_last()
    {
        var r = Region("lokey=60 hikey=60 sw_down=36");
        Assert.Equal(-1, r.SwLast);
        Assert.Equal(36, r.SwDown);
    }

    [Fact]
    public void Off_mode_normal()
    {
        var r = Region("lokey=60 hikey=60 group=1 off_by=1 off_mode=normal");
        Assert.Equal(SamplerOffMode.Normal, r.OffMode);
    }

    [Fact]
    public void Flex_eg_and_lfo_parsed()
    {
        var r = Region("lokey=60 hikey=60 eg1_time0=0.1 eg1_level0=100 eg1_time1=0.2 eg1_level1=50 eg1_cutoff=1200 lfo1_freq=5 lfo1_pitch=20");
        Assert.NotEmpty(r.FlexEgs);
        Assert.NotEmpty(r.FlexLfos);
    }

    [Fact]
    public void Opcode_catalog_classifies_gui_as_ignored()
    {
        Assert.Equal(SfzOpcodeCatalog.Kind.Ignored, SfzOpcodeCatalog.Classify("gui_slider"));
        Assert.Equal(SfzOpcodeCatalog.Kind.Implemented, SfzOpcodeCatalog.Classify("locc64"));
    }
}
