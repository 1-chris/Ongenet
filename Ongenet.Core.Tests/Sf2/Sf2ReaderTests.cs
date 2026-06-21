using Ongenet.Core.Audio.Instruments.Sampler.Sf2;
using Xunit;

namespace Ongenet.Core.Tests.Sf2;

/// <summary>Parser tests against the real bundled SoundFont (skipped if the asset is missing).</summary>
public class Sf2ReaderTests
{
    [Fact]
    public void ParsesHydraAndSamplePool()
    {
        var path = Sf2TestFile.Find();
        if (path is null) return; // asset not present in this environment

        var file = Sf2Reader.Parse(path);

        // Every header array ends with a terminal sentinel, so there is at least one real entry + terminal.
        Assert.True(file.Presets.Count >= 2);
        Assert.True(file.Instruments.Count >= 2);
        Assert.True(file.SampleHeaders.Count >= 2);
        Assert.True(file.PresetBags.Count >= 1);
        Assert.True(file.InstBags.Count >= 1);
        Assert.True(file.PresetGens.Count >= 1);
        Assert.True(file.InstGens.Count >= 1);

        // The sample pool was located and has data.
        Assert.True(file.SmplFrames > 0);

        // Presets are enumerable, sorted, and named (GeneralUser GS has hundreds).
        Assert.True(file.PresetOrder.Count > 0);
        var prev = -1;
        foreach (var p in file.PresetOrder)
        {
            var key = p.Bank * 1000 + p.Program;
            Assert.True(key >= prev, "presets should be sorted by bank then program");
            prev = key;
        }
    }

    [Fact]
    public void TerminalSentinelIsExcludedFromPresetOrder()
    {
        var path = Sf2TestFile.Find();
        if (path is null) return;

        var file = Sf2Reader.Parse(path);
        Assert.Equal(file.Presets.Count - 1, file.PresetOrder.Count);
    }

    [Fact]
    public void ReadingASampleYieldsAudio()
    {
        var path = Sf2TestFile.Find();
        if (path is null) return;

        var file = Sf2Reader.Parse(path);

        // Find a non-empty sample header (skip the terminal) and decode it.
        var any = false;
        for (var i = 0; i < file.SampleHeaders.Count - 1 && !any; i++)
        {
            var h = file.SampleHeaders[i];
            if (h.End <= h.Start) continue;
            var mono = file.ReadMono(h);
            Assert.Equal((int)(h.End - h.Start), mono.Length);
            foreach (var s in mono)
                if (s != 0f) { any = true; break; }
        }

        Assert.True(any, "expected at least one sample with non-zero audio");
    }
}
