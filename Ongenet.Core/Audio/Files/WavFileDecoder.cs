using System;
using System.IO;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Decodes WAV/WAVE files directly (no external tools) via <see cref="WavParser"/>.
/// </summary>
public sealed class WavFileDecoder : IAudioFileDecoder
{
    public bool CanDecode(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".wave", StringComparison.OrdinalIgnoreCase);
    }

    public AudioSampleBuffer Decode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return WavParser.Parse(stream);
    }
}
