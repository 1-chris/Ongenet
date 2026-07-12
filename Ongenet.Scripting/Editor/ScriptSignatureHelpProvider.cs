using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ongenet.Scripting.Editor;

public sealed record ScriptSignatureInfo(string Text, int ActiveParameterIndex, int ParameterCount);

public static class ScriptSignatureHelpProvider
{
    public static async Task<ScriptSignatureInfo?> GetSignatureHelpAsync(
        Document document,
        int caretPosition,
        CancellationToken cancellationToken = default)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return null;

        var token = root.FindToken(caretPosition);
        var invocation = token.Parent?.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation is null) return null;

        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null) return null;

        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
            return null;

        var argIndex = 0;
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.SpanStart <= caretPosition && caretPosition <= arg.Span.End)
                break;
            if (caretPosition > arg.Span.End)
                argIndex++;
        }

        var signatures = method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return new ScriptSignatureInfo(signatures, argIndex, method.Parameters.Length);
    }
}
