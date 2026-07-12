using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

/// <summary>Roslyn C# scripting host with a global <c>api</c> (<see cref="IScriptingApi"/>).</summary>
public sealed class RoslynScriptingHost : IScriptingHost
{
    private readonly ScriptingApi _api;
    private readonly Dictionary<string, Script<object?>> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sources = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeLiveScript;

    public RoslynScriptingHost(ScriptingApi api) => _api = api;

    public bool IsEnabled => true;

    public IReadOnlyList<string> LoadedScripts => _scripts.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    public string? ActiveLiveScript => _activeLiveScript;

    public void LoadScript(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Script path is required.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Script file was not found.", path);

        var name = Path.GetFileNameWithoutExtension(path);
        var code = File.ReadAllText(path);
        RegisterScript(name, code, Path.GetFullPath(path));
    }

    public void LoadScriptFromText(string name, string code, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Script name is required.", nameof(name));
        RegisterScript(name, code, path is null ? null : Path.GetFullPath(path));
    }

    public void UpdateScriptSource(string name, string code)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Script name is required.", nameof(name));
        if (!_scripts.ContainsKey(name))
            throw new InvalidOperationException($"Script '{name}' is not loaded.");
        _scripts[name] = Compile(code);
        _sources[name] = code;
    }

    public void UnloadScript(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_activeLiveScript?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
            StopLive(name);
        _scripts.Remove(name);
        _paths.Remove(name);
        _sources.Remove(name);
    }

    public object? Invoke(string scriptName, string entryPoint, object?[]? args = null)
    {
        if (!_scripts.TryGetValue(scriptName, out var script))
            throw new InvalidOperationException($"Script '{scriptName}' is not loaded.");

        if (!string.IsNullOrEmpty(entryPoint)
            && !entryPoint.Equals("Run", StringComparison.OrdinalIgnoreCase)
            && !entryPoint.Equals("Register", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Entry point '{entryPoint}' is not supported. Use 'Run' or 'Register'.");

        _api.ClearOutput();
        return script.RunAsync(new ScriptGlobals(_api)).GetAwaiter().GetResult().ReturnValue;
    }

    public void StartLive(string scriptName, SynchronizationContext? uiContext = null)
    {
        if (!_scripts.ContainsKey(scriptName))
            throw new InvalidOperationException($"Script '{scriptName}' is not loaded.");

        if (_activeLiveScript is not null && !_activeLiveScript.Equals(scriptName, StringComparison.OrdinalIgnoreCase))
            StopLive(_activeLiveScript);

        _api.ClearOutput();
        _api.BeginLiveSession(uiContext);
        _activeLiveScript = scriptName;
        Invoke(scriptName, "Register");
    }

    public void StopLive(string scriptName)
    {
        if (_activeLiveScript?.Equals(scriptName, StringComparison.OrdinalIgnoreCase) != true)
            return;
        _api.StopLive();
        _activeLiveScript = null;
    }

    public bool IsScriptLive(string name) =>
        _activeLiveScript?.Equals(name, StringComparison.OrdinalIgnoreCase) == true;

    public string? GetScriptPath(string name) =>
        _paths.TryGetValue(name, out var path) ? path : null;

    public string? GetScriptSource(string name)
    {
        if (_sources.TryGetValue(name, out var source))
            return source;
        if (!_paths.TryGetValue(name, out var path) || !File.Exists(path))
            return null;
        return File.ReadAllText(path);
    }

    private void RegisterScript(string name, string code, string? path)
    {
        _scripts[name] = Compile(code);
        _sources[name] = code;
        if (path is not null)
            _paths[name] = path;
    }

    private static Script<object?> Compile(string code)
    {
        var script = CSharpScript.Create<object?>(code, ScriptCompileOptions.CreateOptions(), ScriptCompileOptions.GlobalsType);
        var diagnostics = script.Compile();
        if (diagnostics.Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
        {
            var message = string.Join(Environment.NewLine,
                diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            throw new InvalidOperationException(message);
        }

        return script;
    }

    public sealed class ScriptGlobals
    {
        public ScriptGlobals(IScriptingApi api) => this.api = api;

        public IScriptingApi api { get; }
    }
}
