using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments.Sampler.Sf2;
using Ongenet.Core.Audio.Instruments.Sampler.Sfz;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// Default <see cref="ISamplerLoadService"/>: a thin facade that dispatches by file extension to the
/// format-specific loaders — <see cref="SfzLoader"/> for <c>.sfz</c> and <see cref="Sf2Loader"/> for
/// <c>.sf2</c> — and returns their common <see cref="SamplerLoadResult"/>. Adding a new sound-font format
/// means adding a loader and one branch here; nothing else in the app changes.
/// </summary>
public sealed class SamplerLoadService : ISamplerLoadService
{
    private readonly SfzLoader _sfz;
    private readonly Sf2Loader _sf2;

    public SamplerLoadService(IEnumerable<IAudioFileDecoder> decoders)
    {
        _sfz = new SfzLoader(decoders.ToList());
        _sf2 = new Sf2Loader();
    }

    public SamplerLoadResult? Load(string path, int presetIndex = -1, IProgress<double>? progress = null)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return IsSf2(path) ? _sf2.Load(path, presetIndex, progress) : _sfz.Load(path, progress);
    }

    public SamplerLoadResult? LoadFromText(string sourceText, string path, IProgress<double>? progress = null)
    {
        // Only SFZ carries embedded source text; SF2 is reloaded from disk via Load.
        if (IsSf2(path) || string.IsNullOrEmpty(sourceText)) return null;
        return _sfz.LoadFromText(sourceText, path, progress);
    }

    private static bool IsSf2(string path)
        => string.Equals(Path.GetExtension(path), ".sf2", StringComparison.OrdinalIgnoreCase);
}
