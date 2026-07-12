using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Ongenet.Core.Services;

namespace Ongenet.Scripting.Editor;

/// <summary>In-memory Roslyn workspace for script editing (completion, diagnostics, highlighting).</summary>
public sealed class ScriptEditorWorkspace : IDisposable
{
    private readonly AdhocWorkspace _workspace;
    private readonly DocumentId _documentId;
    private readonly object _gate = new();

    public ScriptEditorWorkspace()
    {
        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        _workspace = new AdhocWorkspace(host);
        var projectId = ProjectId.CreateNewId();
        _documentId = DocumentId.CreateNewId(projectId);
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Script",
            "Script",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(kind: SourceCodeKind.Script),
            metadataReferences: ScriptMetadataReferences.All);
        var documentInfo = DocumentInfo.Create(
            _documentId,
            "Script.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(string.Empty), VersionStamp.Create())));
        _workspace.AddProject(projectInfo);
        _workspace.AddDocument(documentInfo);
    }

    public string Text
    {
        get
        {
            lock (_gate)
            {
                var doc = _workspace.CurrentSolution.GetDocument(_documentId);
                return doc?.GetTextAsync().GetAwaiter().GetResult().ToString() ?? string.Empty;
            }
        }
    }

    public Document Document
    {
        get
        {
            lock (_gate)
                return _workspace.CurrentSolution.GetDocument(_documentId)
                    ?? throw new InvalidOperationException("Script document is missing.");
        }
    }

    public void UpdateText(string text)
    {
        lock (_gate)
        {
            var solution = _workspace.CurrentSolution.WithDocumentText(_documentId, SourceText.From(text));
            _workspace.TryApplyChanges(solution);
        }
    }

    public void Dispose() => _workspace.Dispose();
}

internal static class ScriptMetadataReferences
{
    public static ImmutableArray<MetadataReference> All { get; } =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(IScriptingApi).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ImmutableArray).Assembly.Location)
    ];
}
