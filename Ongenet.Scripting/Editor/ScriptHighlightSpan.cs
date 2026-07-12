namespace Ongenet.Scripting.Editor;

/// <summary>Syntax or diagnostic span for the script editor overlay.</summary>
public readonly record struct ScriptHighlightSpan(int Start, int Length, ScriptHighlightKind Kind);

public enum ScriptHighlightKind
{
    Default,
    Keyword,
    String,
    Comment,
    Type,
    Method,
    Number,
    Error,
    Warning
}
