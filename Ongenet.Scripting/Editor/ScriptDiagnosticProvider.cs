using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ongenet.Scripting.Editor;

public static class ScriptDiagnosticProvider
{
    public static async Task<IReadOnlyList<ScriptDiagnosticInfo>> GetDiagnosticsAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null) return [];

        var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
        if (tree is null) return [];

        var semantic = compilation.GetSemanticModel(tree);
        var list = new List<ScriptDiagnosticInfo>();
        foreach (var d in semantic.GetDiagnostics(cancellationToken: cancellationToken))
        {
            if (d.Location.SourceTree != tree) continue;
            var span = d.Location.SourceSpan;
            list.Add(new ScriptDiagnosticInfo(span.Start, span.Length, d.GetMessage(), d.Severity == DiagnosticSeverity.Error));
        }

        return list;
    }
}
