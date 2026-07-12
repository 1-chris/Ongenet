using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

/// <summary>Roslyn C# scripting host with a global <c>api</c> (<see cref="IScriptingApi"/>).</summary>
public sealed class RoslynScriptingHost : IScriptingHost
{
    private readonly IScriptingApi _api;
    private readonly Dictionary<string, Script<object?>> _scripts = new(StringComparer.OrdinalIgnoreCase);

    public RoslynScriptingHost(IScriptingApi api) => _api = api;

    public bool IsEnabled => true;

    public IReadOnlyList<string> LoadedScripts => _scripts.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    public void LoadScript(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Script path is required.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Script file was not found.", path);

        var name = Path.GetFileNameWithoutExtension(path);
        var code = File.ReadAllText(path);
        var options = ScriptOptions.Default
            .AddReferences(typeof(IScriptingApi).Assembly, typeof(object).Assembly)
            .AddImports("System", "System.Collections.Generic", "Ongenet.Core.Services");

        var script = CSharpScript.Create<object?>(code, options, typeof(ScriptGlobals));
        var diagnostics = script.Compile();
        if (diagnostics.Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
        {
            var message = string.Join(Environment.NewLine,
                diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            throw new InvalidOperationException(message);
        }

        _scripts[name] = script;
    }

    public void UnloadScript(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _scripts.Remove(name);
    }

    public object? Invoke(string scriptName, string entryPoint, object?[]? args = null)
    {
        if (!_scripts.TryGetValue(scriptName, out var script))
            throw new InvalidOperationException($"Script '{scriptName}' is not loaded.");

        if (!string.IsNullOrEmpty(entryPoint)
            && !entryPoint.Equals("Run", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Entry point '{entryPoint}' is not supported. Use 'Run'.");

        return script.RunAsync(new ScriptGlobals(_api)).GetAwaiter().GetResult().ReturnValue;
    }

    public sealed class ScriptGlobals
    {
        public ScriptGlobals(IScriptingApi api) => this.api = api;

        public IScriptingApi api { get; }
    }
}
