using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Ongenet.Core.Services;

namespace Ongenet.Scripting.Editor;

/// <summary>Coordinates workspace analysis for the script editor UI.</summary>
public sealed class ScriptEditorAnalysisService
{
    private readonly ScriptEditorWorkspace _workspace = new();

    public string Text => _workspace.Text;

    public void UpdateText(string text) => _workspace.UpdateText(text);

    public IReadOnlyList<ScriptHighlightSpan> GetHighlightSpans()
        => ScriptSyntaxHighlighter.GetSpans(_workspace.Document);

    public Task<IReadOnlyList<ScriptDiagnosticInfo>> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => ScriptDiagnosticProvider.GetDiagnosticsAsync(_workspace.Document, cancellationToken);

    public Task<IReadOnlyList<ScriptCompletionItem>> GetCompletionsAsync(int caret, CancellationToken cancellationToken = default)
        => ScriptCompletionProvider.GetCompletionsAsync(_workspace.Document, caret, cancellationToken);

    public Task<(string text, int newCaret)?> ApplyCompletionAsync(
        int caret, ScriptCompletionItem item, CancellationToken cancellationToken = default)
        => ScriptCompletionProvider.ApplyCompletionAsync(_workspace.Document, caret, item, cancellationToken);

    public Task<ScriptSignatureInfo?> GetSignatureHelpAsync(int caret, CancellationToken cancellationToken = default)
        => ScriptSignatureHelpProvider.GetSignatureHelpAsync(_workspace.Document, caret, cancellationToken);

    public IReadOnlyList<string> TryCompile(out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            var script = CSharpScript.Create<object?>(
                _workspace.Text,
                ScriptCompileOptions.CreateOptions(),
                ScriptCompileOptions.GlobalsType);
            var diagnostics = script.Compile();
            var errors = diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
            if (errors.Count > 0)
            {
                errorMessage = string.Join(Environment.NewLine, errors);
                return errors;
            }

            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return [ex.Message];
        }
    }

    public void Dispose() => _workspace.Dispose();
}
