using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// Loads an SFZ instrument from disk: reads and parses the file (resolving <c>#include</c> and
/// <c>default_path</c>) and decodes every referenced sample into a <see cref="SfzSampleLibrary"/>.
/// Kept as a Core service (depending on the audio-file decoders) so both the UI and project-load path
/// can rebuild a sampler without the instrument itself touching the file system.
/// </summary>
public interface ISfzLoadService
{
    /// <summary>
    /// Loads from a <c>.sfz</c> file path. Returns null only if the file can't be read at all.
    /// <paramref name="progress"/> (optional) receives 0..1 as samples are loaded.
    /// </summary>
    SfzLoadResult? Load(string sfzPath, IProgress<double>? progress = null);

    /// <summary>
    /// Re-builds from previously persisted SFZ text and the original file path (used on project load when
    /// the on-disk file may have moved/changed). Samples are resolved relative to <paramref name="sfzPath"/>'s
    /// directory.
    /// </summary>
    SfzLoadResult LoadFromText(string sfzText, string sfzPath, IProgress<double>? progress = null);
}

/// <summary>The result of loading an SFZ instrument.</summary>
public sealed class SfzLoadResult
{
    public required SfzDocument Document { get; init; }
    public required SfzSampleLibrary Library { get; init; }

    /// <summary>Absolute path of the loaded <c>.sfz</c> file (may be synthetic when loaded from text).</summary>
    public required string SfzPath { get; init; }

    /// <summary>Raw text of the main <c>.sfz</c> file, persisted so the patch survives without the original file.</summary>
    public required string SfzText { get; init; }

    /// <summary>Display name (file name without extension).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Region sample opcodes whose files could not be found/decoded.</summary>
    public IReadOnlyList<string> MissingSamples { get; init; } = new List<string>();

    /// <summary>Parser + loader warnings (unresolved includes/macros, missing files).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = new List<string>();
}
