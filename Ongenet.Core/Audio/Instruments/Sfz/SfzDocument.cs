using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// A parsed SFZ instrument definition: the <c>&lt;control&gt;</c> settings and the flattened list of
/// playable regions, plus any non-fatal warnings collected while parsing.
/// </summary>
public sealed class SfzDocument
{
    public SfzControl Control { get; init; } = SfzControl.Empty;

    public IReadOnlyList<SfzRegion> Regions { get; init; } = new List<SfzRegion>();

    /// <summary>Non-fatal issues (unresolved includes/macros, malformed directives) for diagnostics.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = new List<string>();
}
