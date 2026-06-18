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
    // Extensions we treat as audio for drag-and-drop and the file browser. WAV decodes natively; the
    // rest are transcoded via ffmpeg. This is the set the browser shows and the timeline accepts.
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".wave",
        ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".mp4", ".aac",
        ".aif", ".aiff", ".aifc", ".wma", ".alac", ".caf", ".ape", ".wv"
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

        // Natural tempo: prefer an explicit "<n>bpm" tag in the file/folder name, else estimate it.
        var named = TempoDetector.FromPath(path);
        var tempo = named ?? TempoDetector.Estimate(samples);
        return new LoadedAudio(samples, waveform, tempo, named.HasValue);
    }
}
