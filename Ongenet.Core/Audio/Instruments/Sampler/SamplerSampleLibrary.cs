using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// The decoded samples backing one loaded sampler patch, keyed by a string id. For SFZ the key is the raw
/// <c>sample</c> opcode (identical strings share one <see cref="SamplerSample"/>); for SF2 it is the sample
/// index. The library keeps the samples alive for the lifetime of the patch and reports whether any are
/// disk-streamed. SFZ large forward one-shots are streamed from the original WAV / a temporary raw file
/// (tracked by <see cref="SamplerTempFiles"/>); short / looped / reversed samples (and all SF2 samples)
/// are resident in RAM.
/// </summary>
public sealed class SamplerSampleLibrary
{
    private readonly Dictionary<string, SamplerSample> _byKey;

    public SamplerSampleLibrary(Dictionary<string, SamplerSample> byKey) => _byKey = byKey;

    /// <summary>Builds a library from an explicit set of samples (SF2: keyed by sample index).</summary>
    public static SamplerSampleLibrary FromSamples(IEnumerable<SamplerSample> samples)
    {
        var dict = new Dictionary<string, SamplerSample>();
        var i = 0;
        foreach (var s in samples) dict[i++.ToString(System.Globalization.CultureInfo.InvariantCulture)] = s;
        return new SamplerSampleLibrary(dict);
    }

    /// <summary>Number of distinct samples.</summary>
    public int Count => _byKey.Count;

    /// <summary>The sample for a key (SFZ <c>sample</c> opcode), or null if it failed to load.</summary>
    public SamplerSample? Get(string key)
        => key.Length > 0 && _byKey.TryGetValue(key, out var s) ? s : null;

    /// <summary>True if any sample is streamed from disk (used to decide whether to register with the engine).</summary>
    public bool HasStreamed
    {
        get
        {
            foreach (var s in _byKey.Values) if (s.IsStreamed) return true;
            return false;
        }
    }

    public static SamplerSampleLibrary Empty { get; } = new(new Dictionary<string, SamplerSample>());
}
