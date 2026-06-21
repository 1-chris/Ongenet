using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>The on-disk format a sampler patch was loaded from.</summary>
public enum SamplerFormat
{
    Sfz,
    Sf2
}

/// <summary>One selectable program inside a multi-preset sound font (SF2). Empty for SFZ.</summary>
public readonly record struct SamplerPresetInfo(int Index, int Bank, int Program, string Name);

/// <summary>
/// Loads a multi-sample instrument from disk and produces ready-to-play <see cref="SamplerRegion"/>s,
/// decoding every referenced sample. Dispatches by file extension to a format-specific loader (SFZ or
/// SF2). Kept as a Core service (depending on the audio-file decoders) so both the UI and the project-load
/// path can rebuild a sampler without the instrument itself touching the file system.
/// </summary>
public interface ISamplerLoadService
{
    /// <summary>
    /// Loads from a <c>.sfz</c> or <c>.sf2</c> file path. <paramref name="presetIndex"/> selects an SF2
    /// preset (-1 = the first preset; ignored for SFZ). Returns null only if the file can't be read/parsed
    /// at all. <paramref name="progress"/> (optional) receives 0..1 as samples are loaded.
    /// </summary>
    SamplerLoadResult? Load(string path, int presetIndex = -1, IProgress<double>? progress = null);

    /// <summary>
    /// Re-builds an SFZ patch from previously persisted source text and the original file path (used on
    /// project load when the on-disk file may have moved). SF2 patches carry no embedded source, so this
    /// returns null for them — they are reloaded via <see cref="Load"/> instead.
    /// </summary>
    SamplerLoadResult? LoadFromText(string sourceText, string path, IProgress<double>? progress = null);
}

/// <summary>The result of loading a sampler instrument: pre-built regions plus the metadata needed to
/// persist, display and (for SF2) switch presets.</summary>
public sealed class SamplerLoadResult
{
    /// <summary>The playable regions for the loaded patch/preset (already bound to decoded samples).</summary>
    public required IReadOnlyList<SamplerRegion> Regions { get; init; }

    /// <summary>The decoded samples (keeps them alive; reports whether any are disk-streamed).</summary>
    public required SamplerSampleLibrary Library { get; init; }

    /// <summary>Absolute path of the loaded file (may be synthetic when an SFZ is loaded from text).</summary>
    public required string Path { get; init; }

    /// <summary>Display name (file name without extension; for SF2 the preset name is appended by the UI).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Which on-disk format this came from.</summary>
    public SamplerFormat Format { get; init; }

    /// <summary>Raw text of an SFZ patch, persisted so it survives without the original file. Empty for SF2.</summary>
    public string SourceText { get; init; } = string.Empty;

    /// <summary>The selected preset index (SF2), or -1 for SFZ.</summary>
    public int PresetIndex { get; init; } = -1;

    /// <summary>All selectable presets in the file (SF2), for the inspector picker. Empty for SFZ.</summary>
    public IReadOnlyList<SamplerPresetInfo> Presets { get; init; } = System.Array.Empty<SamplerPresetInfo>();

    /// <summary>Sample references that could not be found/decoded.</summary>
    public IReadOnlyList<string> MissingSamples { get; init; } = new List<string>();

    /// <summary>Parser + loader warnings.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = new List<string>();
}
