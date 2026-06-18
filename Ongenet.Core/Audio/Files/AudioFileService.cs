using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Default <see cref="IAudioFileService"/>. Recognises common audio extensions for dragging and
/// delegates decoding to the registered <see cref="IAudioFileDecoder"/> implementations.
/// </summary>
public sealed class AudioFileService : IAudioFileService
{
    // Extensions we treat as audio for drag-and-drop. Only those with a registered decoder can
    // actually be decoded today (WAV); the rest are recognised so the UX is ready for them.
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".wave", ".mp3", ".flac", ".ogg", ".aif", ".aiff", ".m4a"
    };

    private readonly IReadOnlyList<IAudioFileDecoder> _decoders;

    public AudioFileService(IEnumerable<IAudioFileDecoder> decoders)
    {
        _decoders = decoders.ToList();
    }

    public bool IsAudioFile(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    public bool CanDecode(string path) => _decoders.Any(d => d.CanDecode(path));

    public LoadedAudio? Load(string path)
    {
        var decoder = _decoders.FirstOrDefault(d => d.CanDecode(path));
        if (decoder is null) return null;

        var samples = decoder.Decode(path);
        var waveform = AudioWaveform.Build(samples);
        return new LoadedAudio(samples, waveform);
    }
}
