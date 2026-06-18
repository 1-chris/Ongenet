namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Decodes an audio file into an <see cref="AudioWaveform"/>. One implementation per format
/// family (WAV today; MP3/FLAC/OGG can be added behind this same seam).
/// </summary>
public interface IAudioFileDecoder
{
    /// <summary>Whether this decoder handles the given file (typically by extension/header).</summary>
    bool CanDecode(string path);

    /// <summary>Decodes the file to interleaved PCM samples.</summary>
    AudioSampleBuffer Decode(string path);
}
