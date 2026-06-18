namespace Ongenet.Core.Audio;

/// <summary>
/// How the input device is captured for recording.
/// </summary>
public enum AudioInputChannelMode
{
    /// <summary>Capture the device's channels as-is (preserves a real stereo source).</summary>
    Stereo,

    /// <summary>
    /// Capture a single channel and store it mono, so it plays centered (equal on both sides) at full
    /// level — the right choice for a mono microphone that only feeds one channel.
    /// </summary>
    Mono
}
