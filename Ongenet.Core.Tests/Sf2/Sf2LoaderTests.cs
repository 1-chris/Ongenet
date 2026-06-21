using System;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Audio.Instruments.Sampler.Sf2;
using Xunit;

namespace Ongenet.Core.Tests.Sf2;

/// <summary>End-to-end SF2 loading + playback against the bundled SoundFont (skipped if absent).</summary>
[Collection("SamplerStaticLoader")]
public class Sf2LoaderTests
{
    private static SamplerLoadResult? Load(int preset = -1)
    {
        var path = Sf2TestFile.Find();
        return path is null ? null : new Sf2Loader().Load(path, preset, null);
    }

    [Fact]
    public void LoadsFirstPresetWithRegionsAndPresetList()
    {
        var result = Load();
        if (result is null) return;

        Assert.Equal(SamplerFormat.Sf2, result.Format);
        Assert.Equal(0, result.PresetIndex);
        Assert.True(result.Presets.Count > 0);
        Assert.True(result.Regions.Count > 0);
        Assert.Equal(string.Empty, result.SourceText); // SF2 carries no embedded source
    }

    [Fact]
    public void RegionsHaveSaneGeometryAndAudio()
    {
        var result = Load();
        if (result is null) return;

        foreach (var r in result.Regions)
        {
            Assert.InRange(r.LoKey, 0, 127);
            Assert.InRange(r.HiKey, r.LoKey, 127);
            Assert.InRange(r.LoVel, 0, 127);
            Assert.InRange(r.HiVel, r.LoVel, 127);
            Assert.InRange(r.PitchKeycenter, 0, 127);
            Assert.True(r.Sample.FrameCount > 0);
        }

        // A General MIDI melodic preset should map middle C at a normal velocity.
        Assert.Contains(result.Regions, r => r.Matches(60, 100));
    }

    [Fact]
    public void RenderProducesSound()
    {
        var result = Load();
        if (result is null) return;

        var inst = new SamplerInstrument();
        inst.Prepare(new AudioFormat(44100, 1));
        inst.ApplyLoad(result);

        inst.NoteOn(60, 1f);
        var buffer = new float[2048];
        inst.Render(buffer);

        Assert.Contains(buffer, s => Math.Abs(s) > 1e-3f);
    }

    [Fact]
    public void SelectingADifferentPresetReloads()
    {
        var first = Load(0);
        if (first is null) return;
        if (first.Presets.Count < 2) return; // need at least two presets to switch

        var second = new Sf2Loader().Load(Sf2TestFile.Find()!, 1, null);
        Assert.NotNull(second);
        Assert.Equal(1, second!.PresetIndex);
        // Different program selected — at minimum the display name reflects the chosen preset.
        Assert.NotEqual(first.PresetIndex, second.PresetIndex);
    }

    [Fact]
    public void InstrumentLoadPreset_SwitchesActivePreset()
    {
        var path = Sf2TestFile.Find();
        if (path is null) return;

        // Provide decoders-free Sf2 loading via the unified service as the static loader.
        var prev = SamplerInstrument.Loader;
        SamplerInstrument.Loader = new SamplerLoadService(Array.Empty<Core.Audio.Files.IAudioFileDecoder>());
        try
        {
            var inst = new SamplerInstrument();
            inst.Prepare(new AudioFormat(44100, 1));
            var loaded = SamplerInstrument.Loader.Load(path, 0);
            Assert.NotNull(loaded);
            inst.ApplyLoad(loaded!);
            Assert.Equal(0, inst.PresetIndex);

            if (inst.Presets.Count >= 2)
            {
                var switched = inst.LoadPreset(1);
                Assert.NotNull(switched);
                Assert.Equal(1, inst.PresetIndex);
            }
        }
        finally { SamplerInstrument.Loader = prev; }
    }
}
