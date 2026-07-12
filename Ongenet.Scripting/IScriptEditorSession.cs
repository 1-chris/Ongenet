using System;
using System.Collections.Generic;
using Ongenet.Scripting.Editor;

namespace Ongenet.Scripting;

/// <summary>Script editor surface exposed to the UI layer.</summary>
public interface IScriptEditorSession : IDisposable
{
    event Action<string>? TextChanged;
    event Action? AnalysisUpdated;

    string Text { get; set; }
    int CaretOffset { get; set; }
    int ErrorCount { get; }
    int WarningCount { get; }
    IReadOnlyList<ScriptCompletionItem> Completions { get; }
    IReadOnlyList<ScriptHighlightSpan> HighlightSpans { get; }
    IReadOnlyList<ScriptDiagnosticInfo> Diagnostics { get; }
    ScriptSignatureInfo? SignatureHelp { get; }

    void LoadText(string text);
    void ShowCompletion();
    bool TryApplyCompletion(ScriptCompletionItem item);
    void RefreshAnalysis();
}
