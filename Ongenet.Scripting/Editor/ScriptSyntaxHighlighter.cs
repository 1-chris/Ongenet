using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Ongenet.Scripting.Editor;

/// <summary>Roslyn classifier-based syntax highlighting for script documents.</summary>
public static class ScriptSyntaxHighlighter
{
    public static IReadOnlyList<ScriptHighlightSpan> GetSpans(Document document, CancellationToken cancellationToken = default)
    {
        var tree = document.GetSyntaxTreeAsync(cancellationToken).GetAwaiter().GetResult();
        if (tree is null) return [];

        var text = tree.GetText(cancellationToken);
        if (text.Length == 0) return [];

        var semantic = document.GetSemanticModelAsync(cancellationToken).GetAwaiter().GetResult();
        if (semantic is null)
            return GetTokenFallbackSpans(tree, cancellationToken);

        var span = TextSpan.FromBounds(0, text.Length);
        var workspace = document.Project.Solution.Workspace;
#pragma warning disable CS0618
        var classified = Classifier.GetClassifiedSpans(semantic, span, workspace, cancellationToken).ToList();
#pragma warning restore CS0618
        var spans = new List<ScriptHighlightSpan>(classified.Count);

        foreach (var item in classified)
        {
            var kind = MapClassification(item.ClassificationType);
            if (kind == ScriptHighlightKind.Default) continue;
            spans.Add(new ScriptHighlightSpan(item.TextSpan.Start, item.TextSpan.Length, kind));
        }

        foreach (var api in tree.GetRoot(cancellationToken).DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (api.Identifier.Text != "api") continue;
            AddOrReplaceSpan(spans, new ScriptHighlightSpan(api.Identifier.SpanStart, api.Identifier.Span.Length, ScriptHighlightKind.Type));
        }

        return spans.OrderBy(s => s.Start).ToList();
    }

    private static ScriptHighlightKind MapClassification(string type) => type switch
    {
        ClassificationTypeNames.Keyword or ClassificationTypeNames.ControlKeyword => ScriptHighlightKind.Keyword,
        ClassificationTypeNames.StringLiteral or ClassificationTypeNames.VerbatimStringLiteral => ScriptHighlightKind.String,
        ClassificationTypeNames.Comment or ClassificationTypeNames.XmlDocCommentName
            or ClassificationTypeNames.XmlDocCommentText or ClassificationTypeNames.XmlDocCommentDelimiter
            or ClassificationTypeNames.XmlDocCommentAttributeName or ClassificationTypeNames.XmlDocCommentAttributeQuotes
            or ClassificationTypeNames.XmlDocCommentAttributeValue or ClassificationTypeNames.XmlDocCommentCDataSection
            or ClassificationTypeNames.PreprocessorText => ScriptHighlightKind.Comment,
        ClassificationTypeNames.NumericLiteral => ScriptHighlightKind.Number,
        ClassificationTypeNames.ClassName or ClassificationTypeNames.StructName or ClassificationTypeNames.InterfaceName
            or ClassificationTypeNames.EnumName or ClassificationTypeNames.DelegateName or ClassificationTypeNames.TypeParameterName
            or ClassificationTypeNames.NamespaceName => ScriptHighlightKind.Type,
        ClassificationTypeNames.MethodName or ClassificationTypeNames.ExtensionMethodName
            or ClassificationTypeNames.PropertyName or ClassificationTypeNames.EventName => ScriptHighlightKind.Method,
        ClassificationTypeNames.EnumMemberName or ClassificationTypeNames.ConstantName => ScriptHighlightKind.Number,
        _ => ScriptHighlightKind.Default
    };

    private static void AddOrReplaceSpan(List<ScriptHighlightSpan> spans, ScriptHighlightSpan span)
    {
        for (var i = spans.Count - 1; i >= 0; i--)
        {
            var existing = spans[i];
            if (existing.Start == span.Start && existing.Length == span.Length)
            {
                spans[i] = span;
                return;
            }

            if (existing.Start < span.Start + span.Length && span.Start < existing.Start + existing.Length)
                spans.RemoveAt(i);
        }

        spans.Add(span);
    }

    private static IReadOnlyList<ScriptHighlightSpan> GetTokenFallbackSpans(SyntaxTree tree, CancellationToken cancellationToken)
    {
        var spans = new List<ScriptHighlightSpan>();
        foreach (var token in tree.GetRoot(cancellationToken).DescendantTokens(descendIntoTrivia: true))
        {
            var kind = ClassifyToken(token);
            if (kind == ScriptHighlightKind.Default) continue;
            spans.Add(new ScriptHighlightSpan(token.SpanStart, token.Span.Length, kind));
        }

        return spans;
    }

    private static ScriptHighlightKind ClassifyToken(SyntaxToken token)
    {
        if (token.IsKind(SyntaxKind.StringLiteralToken) || token.IsKind(SyntaxKind.CharacterLiteralToken))
            return ScriptHighlightKind.String;

        if (token.IsKind(SyntaxKind.SingleLineCommentTrivia) || token.IsKind(SyntaxKind.MultiLineCommentTrivia))
            return ScriptHighlightKind.Comment;

        if (token.IsKind(SyntaxKind.NumericLiteralToken))
            return ScriptHighlightKind.Number;

        if (token.IsKeyword())
            return ScriptHighlightKind.Keyword;

        return ScriptHighlightKind.Default;
    }
}
