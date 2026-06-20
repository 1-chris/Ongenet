using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// The samples for one loaded SFZ instrument, keyed by the raw <c>sample</c> opcode string (identical
/// strings share one <see cref="SfzSample"/>). Large forward one-shots are streamed from temporary raw
/// files (tracked by <see cref="SfzTempFiles"/> and cleaned up on process exit); short / looped /
/// reversed samples are resident in RAM.
/// </summary>
public sealed class SfzSampleLibrary
{
    private readonly Dictionary<string, SfzSample> _bySampleOpcode;

    public SfzSampleLibrary(Dictionary<string, SfzSample> bySampleOpcode)
        => _bySampleOpcode = bySampleOpcode;

    /// <summary>Number of distinct samples.</summary>
    public int Count => _bySampleOpcode.Count;

    /// <summary>The sample for a region's <c>sample</c> opcode value, or null if it failed to load.</summary>
    public SfzSample? Get(string sampleOpcode)
        => sampleOpcode.Length > 0 && _bySampleOpcode.TryGetValue(sampleOpcode, out var s) ? s : null;

    /// <summary>True if any sample is streamed from disk (used to decide whether to register with the engine).</summary>
    public bool HasStreamed
    {
        get
        {
            foreach (var s in _bySampleOpcode.Values) if (s.IsStreamed) return true;
            return false;
        }
    }

    public static SfzSampleLibrary Empty { get; } = new(new Dictionary<string, SfzSample>());
}
