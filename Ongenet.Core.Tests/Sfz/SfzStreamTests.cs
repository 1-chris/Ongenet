using System;
using System.IO;
using System.Runtime.InteropServices;
using Ongenet.Core.Audio.Instruments.Sfz;

namespace Ongenet.Core.Tests.Sfz;

public class SfzStreamTests : IDisposable
{
    private readonly string _path;

    public SfzStreamTests()
    {
        // A mono float32 raw file where frame f holds the value f, so reads are trivially verifiable.
        const int frames = 5000;
        var data = new float[frames];
        for (var i = 0; i < frames; i++) data[i] = i;

        _path = Path.Combine(Path.GetTempPath(), $"ongen-streamtest-{Guid.NewGuid():N}.raw");
        using var fs = File.Create(_path);
        fs.Write(MemoryMarshal.AsBytes(data.AsSpan()));
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* ignore */ }
    }

    [Fact]
    public void ServesPreloadFromRamAndTailFromDisk()
    {
        const long preloadFrames = 100;
        var preload = new float[preloadFrames];
        for (var i = 0; i < preloadFrames; i++) preload[i] = i;

        var sample = SfzSample.FromStream(_path, dataOffset: 0, channels: 1, sampleRate: 44100,
            bits: 32, isFloat: true, frameCount: 5000, preload, preloadFrames);

        var stream = new SfzStream();
        stream.Request(sample, startFrame: 0);
        for (var k = 0; k < 4; k++) stream.Service(); // stand in for the engine thread (open + fill)

        Assert.Equal(0f, stream.Read(0, 0));        // preload (RAM)
        Assert.Equal(50f, stream.Read(50, 0));      // preload
        Assert.Equal(100f, stream.Read(100, 0));    // first streamed frame (disk)
        Assert.Equal(2500f, stream.Read(2500, 0));  // mid stream
        Assert.Equal(4999f, stream.Read(4999, 0));  // last frame
        Assert.Equal(0f, stream.Read(5000, 0));     // out of range

        stream.Close();
    }

    [Fact]
    public void StereoInterleavingIsPreserved()
    {
        // Build a stereo raw file: left = f, right = -f.
        const int frames = 3000;
        var data = new float[frames * 2];
        for (var f = 0; f < frames; f++) { data[f * 2] = f; data[f * 2 + 1] = -f; }
        var path = Path.Combine(Path.GetTempPath(), $"ongen-streamtest-st-{Guid.NewGuid():N}.raw");
        using (var fs = File.Create(path)) fs.Write(MemoryMarshal.AsBytes(data.AsSpan()));

        try
        {
            var sample = SfzSample.FromStream(path, dataOffset: 0, channels: 2, sampleRate: 44100,
                bits: 32, isFloat: true, frameCount: frames, new float[0], 0);
            var stream = new SfzStream();
            stream.Request(sample, 0);
            for (var k = 0; k < 4; k++) stream.Service();

            Assert.Equal(1500f, stream.Read(1500, 0));   // left
            Assert.Equal(-1500f, stream.Read(1500, 1));  // right
            stream.Close();
        }
        finally { try { File.Delete(path); } catch { /* ignore */ } }
    }
}
