namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Front door for audio files: tells callers what's draggable/decodable and loads waveforms by
/// dispatching to the right <see cref="IAudioFileDecoder"/>.
/// </summary>
public interface IAudioFileService
{
    /// <summary>Whether the path looks like an audio file (by extension) — used to enable dragging.</summary>
    bool IsAudioFile(string path);

    /// <summary>Whether a decoder exists that can actually decode this file today.</summary>
    bool CanDecode(string path);

    /// <summary>
    /// Decodes the file to PCM samples (for playback) plus a peak summary (for display), or
    /// returns null if no decoder supports it.
    /// </summary>
    /// <param name="analyzeTempo">
    /// When false, skips expensive onset-based tempo estimation (use for bulk DAW import).
    /// Tagged tempos from the file/folder name are still applied.
    /// </param>
    LoadedAudio? Load(string path, bool analyzeTempo = true);
}
