using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;

namespace Ongenet.Scripting.Editor;

public static class ScriptCompletionProvider
{
    public static async Task<IReadOnlyList<ScriptCompletionItem>> GetCompletionsAsync(
        Document document,
        int caretPosition,
        CancellationToken cancellationToken = default)
    {
        var service = CompletionService.GetService(document);
        if (service is null) return [];

        var results = await service.GetCompletionsAsync(document, caretPosition, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (results is null) return [];

        var list = new List<ScriptCompletionItem>();
        foreach (var item in results.ItemsList.Take(100))
        {
            var description = await service.GetDescriptionAsync(document, item, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var text = item.DisplayText ?? item.FilterText;
            list.Add(new ScriptCompletionItem(text, text, description?.Text));
        }

        return list;
    }

    public static async Task<(string text, int newCaret)?> ApplyCompletionAsync(
        Document document,
        int caretPosition,
        ScriptCompletionItem item,
        CancellationToken cancellationToken = default)
    {
        var service = CompletionService.GetService(document);
        if (service is null) return null;

        var results = await service.GetCompletionsAsync(document, caretPosition, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (results is null) return null;

        var match = results.ItemsList.FirstOrDefault(i =>
            string.Equals(i.DisplayText, item.DisplayText, System.StringComparison.Ordinal));
        if (match is null) return null;

        if (await service.GetChangeAsync(document, match, cancellationToken: cancellationToken).ConfigureAwait(false)
            is not { } change)
            return null;

        var current = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var replaced = current.WithChanges(change.TextChange);
        var newCaret = change.NewPosition ?? (change.TextChange.Span.Start + change.TextChange.NewText?.Length ?? 0);
        return (replaced.ToString(), newCaret);
    }
}
