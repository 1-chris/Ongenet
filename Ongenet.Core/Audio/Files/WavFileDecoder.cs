using System;
using System.IO;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Decodes PCM / IEEE-float WAV/WAVE files directly (no external tools) via <see cref="WavParser"/>.
/// Compressed WAV containers (e.g. FL Studio Edison Ogg-in-WAV) are left for <see cref="FfmpegAudioDecoder"/>.
/// </summary>
public sealed class WavFileDecoder : IAudioFileDecoder
{
    public bool CanDecode(string path)
    {
        var ext = Path.GetExtension(path);
        if (!ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".wave", StringComparison.OrdinalIgnoreCase))
            return false;

        // Unknown / unreadable fmt — assume classic PCM so existing content still loads.
        if (!WavFormatProbe.TryGetFormatTag(path, out var tag))
            return true;

        return WavFormatProbe.IsNativePcmOrFloat(tag);
    }

    public AudioSampleBuffer Decode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return WavParser.Parse(stream);
    }
}
