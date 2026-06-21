using System;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments.Sampler;
using Ongenet.Core.Audio.Instruments.Sampler.Sfz;

namespace Ongenet.Core.Tests.Sfz;

public class SfzLoadServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly ISamplerLoadService _service;

    public SfzLoadServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ongen_sfz_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _service = new SamplerLoadService(new IAudioFileDecoder[] { new WavFileDecoder() });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // Writes a minimal 16-bit mono PCM WAV of constant amplitude.
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

    [Fact]
    public void LoadsSfzAndDecodesReferencedSamples()
    {
        WriteWav(Path.Combine(_dir, "samples", "tone.wav"), 0.5f, 200);
        var sfzPath = Path.Combine(_dir, "kit.sfz");
        File.WriteAllText(sfzPath, "<region> sample=samples/tone.wav key=60");

        var result = _service.Load(sfzPath);

        Assert.NotNull(result);
        Assert.Single(result!.Regions);
        Assert.Equal(1, result.Library.Count);
        Assert.Empty(result.MissingSamples);

        // Drive it through the instrument end-to-end.
        var inst = new SamplerInstrument();
        inst.Prepare(new AudioFormat(44100, 1));
        inst.ApplyLoad(result);
        inst.NoteOn(60, 1f);
        var buffer = new float[64];
        inst.Render(buffer);
        Assert.True(buffer[0] > 0.4f);
    }

    [Fact]
    public void ResolvesSamplePathsCaseInsensitively()
    {
        WriteWav(Path.Combine(_dir, "samples", "tone.wav"), 0.5f, 100);
        var sfzPath = Path.Combine(_dir, "kit.sfz");
        // Reference with the wrong case + backslashes (Windows-authored library on Linux).
        File.WriteAllText(sfzPath, @"<region> sample=Samples\TONE.WAV key=60");

        var result = _service.Load(sfzPath);

        Assert.NotNull(result);
        Assert.Empty(result!.MissingSamples);
        Assert.Equal(1, result.Library.Count);
    }

    [Fact]
    public void MissingSampleIsReportedNotFatal()
    {
        var sfzPath = Path.Combine(_dir, "kit.sfz");
        File.WriteAllText(sfzPath, "<region> sample=does_not_exist.wav key=60");

        var result = _service.Load(sfzPath);

        Assert.NotNull(result);
        Assert.Contains("does_not_exist.wav", result!.MissingSamples);
        Assert.Equal(0, result.Library.Count);
    }

    [Fact]
    public void LargeSampleIsStreamedSmallIsResident()
    {
        WriteWav(Path.Combine(_dir, "short.wav"), 0.5f, 44100);       // 1s -> resident
        WriteWav(Path.Combine(_dir, "long.wav"), 0.5f, 44100 * 8);    // 8s -> streamed
        var sfzPath = Path.Combine(_dir, "kit.sfz");
        File.WriteAllText(sfzPath, "<region> sample=short.wav key=60\n<region> sample=long.wav key=62");

        var result = _service.Load(sfzPath)!;

        Assert.False(result.Library.Get("short.wav")!.IsStreamed);
        var streamed = result.Library.Get("long.wav")!;
        Assert.True(streamed.IsStreamed);
        Assert.NotNull(streamed.StreamPath);
        Assert.True(File.Exists(streamed.StreamPath!));
        Assert.True(streamed.PreloadFrames is > 0 and <= 44100); // ~0.5s preload
    }

    [Fact]
    public void LoopedSampleStaysResidentEvenIfLarge()
    {
        WriteWav(Path.Combine(_dir, "pad.wav"), 0.5f, 44100 * 10);    // 10s but looped
        var sfzPath = Path.Combine(_dir, "kit.sfz");
        File.WriteAllText(sfzPath, "<region> sample=pad.wav key=60 loop_mode=loop_continuous");

        var result = _service.Load(sfzPath)!;
        Assert.False(result.Library.Get("pad.wav")!.IsStreamed); // looping forces residency
    }

    [Fact]
    public void AppliesDefaultPathPrefix()
    {
        WriteWav(Path.Combine(_dir, "wav", "a.wav"), 0.5f, 100);
        var sfzPath = Path.Combine(_dir, "kit.sfz");
        File.WriteAllText(sfzPath, "<control> default_path=wav/\n<region> sample=a.wav key=60");

        var result = _service.Load(sfzPath);

        Assert.NotNull(result);
        Assert.Empty(result!.MissingSamples);
        Assert.Equal(1, result.Library.Count);
    }
}
