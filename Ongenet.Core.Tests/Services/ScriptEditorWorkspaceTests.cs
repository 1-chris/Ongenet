using System.Linq;
using System.Threading.Tasks;
using Ongenet.Scripting.Editor;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class ScriptEditorWorkspaceTests
{
    [Fact]
    public async Task Completion_AfterSystemDot_ReturnsTypes()
    {
        using var workspace = new ScriptEditorWorkspace();
        workspace.UpdateText("System.");
        var caret = "System.".Length;

        var completions = await ScriptCompletionProvider.GetCompletionsAsync(workspace.Document, caret);

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.DisplayText == "Action");
    }

    [Fact]
    public void SyntaxHighlighter_ClassifiesStringLiterals()
    {
        using var workspace = new ScriptEditorWorkspace();
        workspace.UpdateText("var message = \"hello\";");

        var spans = ScriptSyntaxHighlighter.GetSpans(workspace.Document);

        Assert.Contains(spans, s => s.Kind == ScriptHighlightKind.String);
    }
}
