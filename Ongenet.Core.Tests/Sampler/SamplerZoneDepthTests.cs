using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Audio.Instruments.Sampler.Sfz;

namespace Ongenet.Core.Tests.Sampler;

public class SamplerZoneDepthTests
{
    private static AudioSampleBuffer Alternating(float amp, int frames)
    {
        var s = new float[frames];
        for (var i = 0; i < frames; i++) s[i] = i % 2 == 0 ? amp : -amp;
        return new AudioSampleBuffer(s, 1, 44100);
    }

    private static SamplerEgSpec InstantAmpEg => new()
    {
        Attack = 0,
        Decay = 0,
        Sustain = 1,
        Release = 0.01
    };

    private static SamplerRegion MakeRegion(
        SamplerSample sample,
        int key,
        float polarity = 1f,
        bool hasFilter = false,
        double cutoff = 20_000,
        FilterMode filterMode = FilterMode.LowPass,
        double filterQ = 0.707,
        int seqLength = 1,
        int seqPosition = 1,
        int roundRobinKey = 0)
        => new()
        {
            Sample = sample,
            LoKey = key,
            HiKey = key,
            LoVel = 0,
            HiVel = 127,
            PitchKeycenter = key,
            KeytrackSemisPerKey = 1,
            Gain = polarity,
            AmpVeltrack = 0,
            AmpEg = InstantAmpEg,
            End = sample.FrameCount,
            HasFilter = hasFilter,
            FilterMode = filterMode,
            Cutoff = cutoff,
            FilterQ = filterQ,
            SeqLength = seqLength,
            SeqPosition = seqPosition,
            RoundRobinKey = roundRobinKey
        };

    private static SamplerInstrument MakeInstrument(params SamplerRegion[] regions)
    {
        var inst = new SamplerInstrument();
        inst.Prepare(new AudioFormat(44100, 1));
        inst.ReplaceRegions(regions);
        return inst;
    }

    private static SamplerInstrument MakeFromSfzRegions(string sfz, params (string name, AudioSampleBuffer buf)[] samples)
    {
        var doc = SfzParser.Parse(sfz);
        var dict = new Dictionary<string, SamplerSample>();
        foreach (var (name, buf) in samples) dict[name] = SamplerSample.FromResident(buf);
        var lib = new SamplerSampleLibrary(dict);
        return MakeInstrument(SfzLoader.BuildRegions(doc, lib).ToArray());
    }

    private static AudioSampleBuffer Const(float v, int frames)
        => new(Enumerable.Repeat(v, frames).ToArray(), 1, 44100);

    private static float[] Render(SamplerInstrument inst, int frames)
    {
        var buffer = new float[frames];
        inst.Render(buffer);
        return buffer;
    }

    private static double Rms(float[] samples)
    {
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return Math.Sqrt(sum / samples.Length);
    }

    [Fact]
    public void ZoneLowPassFilterAttenuatesNyquistTone()
    {
        var sample = SamplerSample.FromResident(Alternating(0.5f, 400));
        var dark = MakeInstrument(MakeRegion(sample, 60, hasFilter: true, cutoff: 300));
        var bright = MakeInstrument(MakeRegion(sample, 60, hasFilter: true, cutoff: 12_000));

        dark.NoteOn(60, 1f);
        var darkLevel = Rms(Render(dark, 400));

        bright.NoteOn(60, 1f);
        var brightLevel = Rms(Render(bright, 400));

        Assert.True(brightLevel > darkLevel * 2,
            $"expected brighter output with higher cutoff (bright={brightLevel:F4}, dark={darkLevel:F4})");
    }

    [Fact]
    public void ZoneHighPassFilterBlocksDcTone()
    {
        var sample = SamplerSample.FromResident(new AudioSampleBuffer(Enumerable.Repeat(0.5f, 400).ToArray(), 1, 44100));
        var filtered = MakeInstrument(MakeRegion(sample, 60, hasFilter: true, cutoff: 800, filterMode: FilterMode.HighPass));
        var bypass = MakeInstrument(MakeRegion(sample, 60));

        filtered.NoteOn(60, 1f);
        var filteredLevel = Rms(Render(filtered, 400));

        bypass.NoteOn(60, 1f);
        var bypassLevel = Rms(Render(bypass, 400));

        Assert.True(bypassLevel > filteredLevel * 3,
            $"expected HP filter to suppress DC (bypass={bypassLevel:F4}, filtered={filteredLevel:F4})");
    }

    [Fact]
    public void RoundRobinGroupAlternatesMatchingZones()
    {
        const string sfz = @"
<group>
<region> sample=rr1.wav key=60 seq_length=2 seq_position=1 amp_veltrack=0
<region> sample=rr2.wav key=60 seq_length=2 seq_position=2 amp_veltrack=0";

        var inst = MakeFromSfzRegions(sfz,
            ("rr1.wav", Const(0.5f, 100)),
            ("rr2.wav", Const(-0.5f, 100)));

        Assert.Equal(2, inst.Regions.Count);
        Assert.Equal(inst.Regions[0].RoundRobinKey, inst.Regions[1].RoundRobinKey);

        inst.NoteOn(60, 1f);
        Assert.True(Render(inst, 128)[0] > 0.4f);

        inst.NoteOff(60);
        Render(inst, 256);

        inst.NoteOn(60, 1f);
        Assert.True(Render(inst, 128)[0] < -0.4f);
    }

    [Fact]
    public void RoundRobinGroupsCycleIndependently()
    {
        var pos = Const(0.5f, 80);
        var neg = Const(-0.5f, 80);

        var inst = MakeInstrument(
            MakeRegion(SamplerSample.FromResident(pos), 60, seqLength: 2, seqPosition: 1, roundRobinKey: 1),
            MakeRegion(SamplerSample.FromResident(neg), 60, seqLength: 2, seqPosition: 2, roundRobinKey: 1),
            MakeRegion(SamplerSample.FromResident(pos), 61, seqLength: 2, seqPosition: 1, roundRobinKey: 2),
            MakeRegion(SamplerSample.FromResident(neg), 61, seqLength: 2, seqPosition: 2, roundRobinKey: 2));

        Assert.All(inst.Regions, r => Assert.Equal(2, r.SeqLength));

        inst.NoteOn(60, 1f);
        var hit60a = Render(inst, 64)[0];
        inst.NoteOff(60);
        Render(inst, 256);

        inst.NoteOn(61, 1f);
        var hit61a = Render(inst, 64)[0];
        inst.NoteOff(61);
        Render(inst, 256);

        inst.NoteOn(60, 1f);
        var hit60b = Render(inst, 64)[0];
        inst.NoteOff(60);
        Render(inst, 256);

        inst.NoteOn(61, 1f);
        var hit61b = Render(inst, 64)[0];

        Assert.True(hit60a > 0.4f, $"first C4 hit expected positive sample (got {hit60a:F4})");
        Assert.True(hit61a > 0.4f, $"first C#4 hit expected positive sample (got {hit61a:F4})");
        Assert.True(hit60b < -0.4f, $"second C4 hit expected negative sample (got {hit60b:F4})");
        Assert.True(hit61b < -0.4f, $"second C#4 hit expected negative sample (got {hit61b:F4})");
    }

    [Fact]
    public void ExplicitRoundRobinKeyGroupsZones()
    {
        var loud = SamplerSample.FromResident(Const(0.8f, 100));
        var quiet = SamplerSample.FromResident(Const(0.2f, 100));
        const int rrGroup = 99;

        var inst = MakeInstrument(
            MakeRegion(loud, 60, seqLength: 2, seqPosition: 1, roundRobinKey: rrGroup),
            MakeRegion(quiet, 60, seqLength: 2, seqPosition: 2, roundRobinKey: rrGroup));

        inst.NoteOn(60, 1f);
        var first = Render(inst, 64)[0];
        inst.NoteOff(60);
        Render(inst, 256);

        inst.NoteOn(60, 1f);
        var second = Render(inst, 64)[0];

        Assert.True(first > 0.7f, $"first RR hit expected loud zone (got {first:F4})");
        Assert.InRange(second, 0.15, 0.35);
        Assert.True(first > second * 2);
    }

    [Fact]
    public void ReplaceRegionsAppliesEditedFilterAndRoundRobinKey()
    {
        var sample = SamplerSample.FromResident(Alternating(0.5f, 400));
        var baseRegion = MakeRegion(sample, 60);
        var inst = MakeInstrument(baseRegion);

        var edited = baseRegion.Copy(
            hasFilter: true,
            cutoff: 250,
            filterQ: 1.2,
            filterMode: FilterMode.LowPass,
            seqLength: 2,
            seqPosition: 1,
            roundRobinKey: 7);
        inst.ReplaceRegions(new[] { edited });

        var restored = inst.Regions.Single();
        Assert.True(restored.HasFilter);
        Assert.Equal(250, restored.Cutoff, 3);
        Assert.Equal(1.2, restored.FilterQ, 3);
        Assert.Equal(FilterMode.LowPass, restored.FilterMode);
        Assert.Equal(2, restored.SeqLength);
        Assert.Equal(1, restored.SeqPosition);
        Assert.Equal(7, restored.RoundRobinKey);
    }
}
