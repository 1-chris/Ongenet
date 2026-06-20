namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>The two kinds of lexical token an SFZ file is built from.</summary>
public enum SfzTokenKind
{
    /// <summary>A section header such as <c>&lt;region&gt;</c>. <see cref="SfzToken.Name"/> is the header name (no brackets).</summary>
    Header,

    /// <summary>An <c>opcode=value</c> pair.</summary>
    Opcode
}

/// <summary>
/// A single SFZ token: either a header or an opcode/value pair. Opcode names are lower-cased for
/// case-insensitive lookup; values keep their original case (sample paths are case-sensitive).
/// </summary>
public readonly record struct SfzToken(SfzTokenKind Kind, string Name, string Value)
{
    public static SfzToken Header(string name) => new(SfzTokenKind.Header, name.ToLowerInvariant(), string.Empty);
    public static SfzToken Opcode(string name, string value) => new(SfzTokenKind.Opcode, name.ToLowerInvariant(), value);
}
