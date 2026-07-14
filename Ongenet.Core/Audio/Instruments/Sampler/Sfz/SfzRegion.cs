namespace Ongenet.Core.Audio.Instruments.Sampler.Sfz;

/// <summary>
/// One playable SFZ <c>&lt;region&gt;</c> with its opcodes already flattened through the
/// <c>&lt;global&gt; → &lt;master&gt; → &lt;group&gt;</c> inheritance chain (nearer scopes win). The
/// engine reads this via the typed accessors on <see cref="Opcodes"/>.
/// </summary>
public sealed class SfzRegion
{
    /// <summary>Zero-based index of this region within the document (parse order).</summary>
    public int Index { get; init; }

    /// <summary>The group index this region belongs to, or -1 if it has no <c>&lt;group&gt;</c>.</summary>
    public int GroupIndex { get; init; } = -1;

    /// <summary>
    /// <c>default_path</c> from the <c>&lt;control&gt;</c> header that was active when this region
    /// was defined. Keyswitch banks often redefine <c>default_path</c> per articulation; each region
    /// keeps the path that applied to it (not only the final control block).
    /// </summary>
    public string DefaultPath { get; init; } = string.Empty;

    /// <summary>The effective (inheritance-flattened) opcode set for this region.</summary>
    public SfzOpcodes Opcodes { get; init; }

    /// <summary>The raw <c>sample</c> opcode value as written (before <c>default_path</c> is applied).</summary>
    public string Sample => Opcodes.Get("sample", string.Empty);

    /// <summary>Stable sample lookup key: default path + sample name (paths may reuse filenames).</summary>
    public string SampleKey => DefaultPath.Length == 0 ? Sample : DefaultPath + "\0" + Sample;
}
