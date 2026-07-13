using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
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
    // Must be initialized before All — FromAssembly reads this during the All field initializer.
    private static readonly string[] TrustedAssemblies = LoadTrustedAssemblies();

    public static ImmutableArray<MetadataReference> All { get; } =
    [
        FromAssembly(typeof(object).Assembly),
        FromAssembly(typeof(IScriptingApi).Assembly),
        FromAssembly(typeof(Console).Assembly),
        FromAssembly(typeof(System.Linq.Enumerable).Assembly),
        FromAssembly(typeof(ImmutableArray).Assembly)
    ];

    private static string[] LoadTrustedAssemblies()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        return string.IsNullOrEmpty(tpa)
            ? Array.Empty<string>()
            : tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
    }

    private static MetadataReference FromAssembly(Assembly assembly)
    {
        var location = assembly.Location;
        if (!string.IsNullOrEmpty(location) && File.Exists(location))
            return MetadataReference.CreateFromFile(location);

        var simpleName = assembly.GetName().Name;
        if (string.IsNullOrEmpty(simpleName))
        {
            throw new InvalidOperationException(
                $"Cannot create a Roslyn metadata reference for '{assembly.FullName}' (empty Location and no simple name).");
        }

        var fromTrusted = TrustedAssemblies.FirstOrDefault(path =>
            string.Equals(Path.GetFileNameWithoutExtension(path), simpleName, StringComparison.OrdinalIgnoreCase));
        if (fromTrusted is not null && File.Exists(fromTrusted))
            return MetadataReference.CreateFromFile(fromTrusted);

        foreach (var dir in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(Environment.ProcessPath) })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var sidecar = Path.Combine(dir, simpleName + ".dll");
            if (File.Exists(sidecar))
                return MetadataReference.CreateFromFile(sidecar);
        }

        throw new InvalidOperationException(
            $"Cannot create a Roslyn metadata reference for '{assembly.FullName}' " +
            "(Assembly.Location is empty and no on-disk assembly was found).");
    }
}
