namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Decoded PCM audio held in memory: interleaved 32-bit float samples at the file's own sample
/// rate and channel count. The engine reads (and resamples) these to play audio clips.
/// </summary>
public sealed class AudioSampleBuffer
{
    public AudioSampleBuffer(float[] samples, int channels, int sampleRate)
    {
        Samples = samples;
        Channels = channels < 1 ? 1 : channels;
        SampleRate = sampleRate <= 0 ? 44100 : sampleRate;
    }

    /// <summary>Interleaved samples (frame-major: frame f, channel c is at f*Channels + c).</summary>
    public float[] Samples { get; }

    public int Channels { get; }

    public int SampleRate { get; }

    /// <summary>Number of frames (samples per channel).</summary>
    public long FrameCount => Samples.Length / Channels;

    /// <summary>Reads one channel's sample at a frame, clamped to range (0 outside the buffer).</summary>
    public float Sample(long frame, int channel)
    {
        if (frame < 0 || frame >= FrameCount) return 0f;
        if (channel >= Channels) channel = Channels - 1;
        return Samples[frame * Channels + channel];
    }
}
