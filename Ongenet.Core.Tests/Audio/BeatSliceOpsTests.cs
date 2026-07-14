using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments.Sampler;

namespace Ongenet.Core.Tests.Audio;

public sealed class BeatSliceOpsTests
{
    [Fact]
    public void EqualDivisions_builds_regular_regions()
    {
        var buffer = MakeImpulseTrain(44100, 4410);
        BeatSliceOps.SliceToGrid(buffer, BeatSliceDetectMode.EqualDivisions, secondsPerBeat: 0.5, divisionsPerBeat: 2);

        Assert.Equal(4, buffer.SliceRegions.Count);
        Assert.Equal(0, buffer.SliceRegions[0].StartFrame);
        Assert.Equal(11025, buffer.SliceRegions[0].EndFrame);
    }

    [Fact]
    public void TransientDetection_finds_impulses()
    {
        var buffer = MakeImpulseTrain(44100, 4410);
        BeatSliceOps.SliceToGrid(buffer, BeatSliceDetectMode.Transients, secondsPerBeat: 0.5,
            transientSensitivity: 0.2, minGapSeconds: 0.05);

        Assert.True(buffer.SliceRegions.Count >= 3);
    }

    [Fact]
    public void ReorderRegion_changes_export_order()
    {
        var buffer = MakeImpulseTrain(44100, 4410);
        BeatSliceOps.SliceToGrid(buffer, BeatSliceDetectMode.EqualDivisions, secondsPerBeat: 0.5, divisionsPerBeat: 2);
        BeatSliceOps.MoveRegionDown(buffer, 0);

        var ordered = BeatSliceOps.OrderedRegions(buffer);
        Assert.Equal(11025, ordered[0].StartFrame);
    }

    [Fact]
    public void Export_builds_one_zone_per_selected_slice()
    {
        var buffer = MakeImpulseTrain(44100, 4410);
        BeatSliceOps.SliceToGrid(buffer, BeatSliceDetectMode.EqualDivisions, secondsPerBeat: 0.5, divisionsPerBeat: 2);
        buffer.SliceRegions[0].Selected = false;

        var regions = SamplerSliceExport.BuildRegions(buffer, buffer.SliceRegions, rootKeyStart: 60);
        Assert.Equal(3, regions.Count);
        Assert.Equal(60, regions[0].LoKey);
    }

    private static AudioSampleBuffer MakeImpulseTrain(int sampleRate, int hop)
    {
        var samples = new float[sampleRate];
        for (var i = 0; i < samples.Length; i += hop)
            samples[i] = 1f;
        return new AudioSampleBuffer(samples, 1, sampleRate);
    }
}
