using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// A sample referenced by an SFZ patch, in one of two tiers:
/// <list type="bullet">
/// <item><b>Resident</b> — fully decoded in RAM (<see cref="Resident"/>). Used for short samples and any
/// sample played looped or reversed (random access needed).</item>
/// <item><b>Streamed</b> — only the attack (<see cref="Preload"/>) is kept in RAM; the body is read on
/// demand from <see cref="StreamPath"/> at <see cref="StreamDataOffset"/> in its native PCM format
/// (<see cref="StreamBits"/>/<see cref="StreamIsFloat"/>) and converted while streaming. For WAV this is
/// the original file (no decode/copy at load); for other formats it's a float32 raw cache file.</item>
/// </list>
/// </summary>
public sealed class SamplerSample
{
    public int Channels { get; }
    public int SampleRate { get; }
    public long FrameCount { get; }

    /// <summary>Non-null when the sample is fully resident in RAM.</summary>
    public AudioSampleBuffer? Resident { get; }

    /// <summary>Path read by the streaming engine (original WAV or a float32 raw cache), else null.</summary>
    public string? StreamPath { get; }
    public long StreamDataOffset { get; }
    public int StreamBits { get; }
    public bool StreamIsFloat { get; }

    /// <summary>The first <see cref="PreloadFrames"/> frames, interleaved, kept in RAM for instant note starts.</summary>
    public float[]? Preload { get; }
    public long PreloadFrames { get; }

    public bool IsStreamed => StreamPath is not null;

    private SamplerSample(int channels, int sampleRate, long frameCount, AudioSampleBuffer? resident,
        string? streamPath, long dataOffset, int bits, bool isFloat, float[]? preload, long preloadFrames)
    {
        Channels = channels;
        SampleRate = sampleRate;
        FrameCount = frameCount;
        Resident = resident;
        StreamPath = streamPath;
        StreamDataOffset = dataOffset;
        StreamBits = bits;
        StreamIsFloat = isFloat;
        Preload = preload;
        PreloadFrames = preloadFrames;
    }

    public static SamplerSample FromResident(AudioSampleBuffer buffer)
        => new(buffer.Channels, buffer.SampleRate, buffer.FrameCount, buffer, null, 0, 0, false, null, 0);

    /// <summary>A sample streamed from <paramref name="path"/> in its native PCM format.</summary>
    public static SamplerSample FromStream(string path, long dataOffset, int channels, int sampleRate, int bits,
        bool isFloat, long frameCount, float[] preload, long preloadFrames)
        => new(channels, sampleRate, frameCount, null, path, dataOffset, bits, isFloat, preload, preloadFrames);
}
