using System;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Sampler;

[Collection("SamplerStaticLoader")]
public class SamplerLayerTests : IDisposable
{
    private readonly string _dir;
    private readonly ISamplerLoadService _service;
    private readonly ISamplerLoadService? _prevLoader;

    public SamplerLayerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ongen_layers_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _service = new SamplerLoadService(new IAudioFileDecoder[] { new WavFileDecoder() });
        _prevLoader = SamplerInstrument.Loader;
        SamplerInstrument.Loader = _service;
    }

    public void Dispose()
    {
        SamplerInstrument.Loader = _prevLoader;
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static void WriteWav(string path, float amplitude, int frames, int sampleRate = 44100)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sample = (short)(amplitude * short.MaxValue);
        var dataLen = frames * 2;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataLen);
        for (var i = 0; i < frames; i++) w.Write(sample);
    }

    private SamplerLoadResult LoadKeyed(string name, int key)
    {
        var sampleDir = Path.Combine(_dir, name);
        WriteWav(Path.Combine(sampleDir, "tone.wav"), 0.5f, 200);
        var sfz = Path.Combine(_dir, name + ".sfz");
        File.WriteAllText(sfz, $"<region> sample={name}/tone.wav key={key}");
        var result = _service.Load(sfz);
        Assert.NotNull(result);
        return result!;
    }

    [Fact]
    public void AddLayerStacksRegionsFromBothPatches()
    {
        var a = LoadKeyed("low", 48);
        var b = LoadKeyed("high", 72);
        var inst = new SamplerInstrument();
        inst.ApplyLoad(a);
        inst.AddLayer(b);

        Assert.Equal(2, inst.LayerCount);
        Assert.Equal(2, inst.Regions.Count);
        Assert.Contains(inst.Regions, r => r.Matches(48, 100));
        Assert.Contains(inst.Regions, r => r.Matches(72, 100));
        Assert.Equal(2, inst.Regions.Select(r => r.LayerId).Distinct().Count());
    }

    [Fact]
    public void RemoveLayerDropsItsRegions()
    {
        var a = LoadKeyed("a", 60);
        var b = LoadKeyed("b", 61);
        var inst = new SamplerInstrument();
        inst.ApplyLoad(a);
        inst.AddLayer(b);
        var id = inst.Layers[1].Id;
        Assert.True(inst.RemoveLayer(id));
        Assert.Single(inst.Layers);
        Assert.Single(inst.Regions);
        Assert.Contains(inst.Regions, r => r.Matches(60, 100));
    }

    [Fact]
    public void KeyMaskClipsPlayableRange()
    {
        var a = LoadKeyed("wide", 60);
        // Expand the loaded region via rewrite for a wider patch
        var sfz = Path.Combine(_dir, "wide2.sfz");
        WriteWav(Path.Combine(_dir, "wide2", "tone.wav"), 0.5f, 200);
        File.WriteAllText(sfz, "<region> sample=wide2/tone.wav lokey=36 hikey=84 pitch_keycenter=60");
        var wide = _service.Load(sfz)!;
        var inst = new SamplerInstrument();
        inst.ApplyLoad(wide);
        var id = inst.Layers[0].Id;
        Assert.True(inst.SetLayerKeyMask(id, 48, 60));
        Assert.Contains(inst.Regions, r => r.Matches(48, 100));
        Assert.Contains(inst.Regions, r => r.Matches(60, 100));
        Assert.DoesNotContain(inst.Regions, r => r.Matches(36, 100));
        Assert.DoesNotContain(inst.Regions, r => r.Matches(72, 100));
    }

    [Fact]
    public void DisableLayerRemovesItsRegionsFromPlayback()
    {
        var a = LoadKeyed("on", 40);
        var b = LoadKeyed("off", 41);
        var inst = new SamplerInstrument();
        inst.ApplyLoad(a);
        inst.AddLayer(b);
        var id = inst.Layers[1].Id;
        Assert.True(inst.SetLayerEnabled(id, false));
        Assert.Equal(2, inst.LayerCount);
        Assert.Single(inst.Regions);
        Assert.DoesNotContain(inst.Regions, r => r.Matches(41, 100));
    }

    [Fact]
    public void ProjectStateRoundTripsLayers()
    {
        var a = LoadKeyed("p1", 50);
        var b = LoadKeyed("p2", 51);
        var inst = new SamplerInstrument { MasterGain = 0.8 };
        inst.ApplyLoad(a);
        inst.AddLayer(b);
        var id0 = inst.Layers[0].Id;
        Assert.True(inst.SetLayerColor(id0, 0xFFAABBCC));

        using var ms = new MemoryStream();
        using (var w = new OngenWriter(ms))
            inst.WriteProjectState(w);
        ms.Position = 0;
        var restored = new SamplerInstrument();
        using (var r = new OngenReader(ms))
            restored.ReadProjectState(r);

        Assert.Equal(0.8, restored.MasterGain, 3);
        Assert.Equal(2, restored.LayerCount);
        Assert.Equal(2, restored.Regions.Count);
        Assert.Contains(restored.Regions, region => region.Matches(50, 100));
        Assert.Contains(restored.Regions, region => region.Matches(51, 100));
        Assert.Equal(0xFFAABBCC, restored.Layers[0].ColorArgb);
        Assert.NotEqual(restored.Layers[0].ColorArgb, restored.Layers[1].ColorArgb);
    }
}
