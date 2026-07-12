using System;
using System.Collections.Generic;

namespace Ongenet.Core.Services;

/// <summary>User scripting / M4L-class extension host (Phase 6).</summary>
public interface IScriptingHost
{
    bool IsEnabled { get; }
    IReadOnlyList<string> LoadedScripts { get; }
    void LoadScript(string path);
    void UnloadScript(string name);
    object? Invoke(string scriptName, string entryPoint, object?[]? args = null);
}

public sealed class NullScriptingHost : IScriptingHost
{
    public bool IsEnabled => false;
    public IReadOnlyList<string> LoadedScripts => Array.Empty<string>();
    public void LoadScript(string path) => throw new NotSupportedException("Scripting is not enabled.");
    public void UnloadScript(string name) { }
    public object? Invoke(string scriptName, string entryPoint, object?[]? args = null) => null;
}
