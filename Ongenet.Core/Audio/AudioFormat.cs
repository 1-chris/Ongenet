namespace Ongenet.Core.Audio;

/// <summary>
/// The PCM format the engine runs at: sample rate and channel count. Samples are 32-bit
/// float, interleaved by channel.
/// </summary>
public readonly record struct AudioFormat(int SampleRate, int Channels)
{
    /// <summary>A common default: 44.1 kHz stereo.</summary>
    public static AudioFormat Default => new(44100, 2);
}
