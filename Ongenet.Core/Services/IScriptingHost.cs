using System;
using System.Collections.Generic;
using System.Threading;

namespace Ongenet.Core.Services;

/// <summary>User scripting / M4L-class extension host (Phase 6).</summary>
public interface IScriptingHost
{
    bool IsEnabled { get; }
    IReadOnlyList<string> LoadedScripts { get; }
    string? ActiveLiveScript { get; }
    void LoadScript(string path);
    void LoadScriptFromText(string name, string code, string? path = null);
    void UpdateScriptSource(string name, string code);
    void UnloadScript(string name);
    object? Invoke(string scriptName, string entryPoint, object?[]? args = null);
    void StartLive(string scriptName, SynchronizationContext? uiContext = null);
    void StopLive(string scriptName);
    bool IsScriptLive(string name);
    string? GetScriptPath(string name);
    string? GetScriptSource(string name);
}

public sealed class NullScriptingHost : IScriptingHost
{
    public bool IsEnabled => false;
    public IReadOnlyList<string> LoadedScripts => Array.Empty<string>();
    public string? ActiveLiveScript => null;
    public void LoadScript(string path) => throw new NotSupportedException("Scripting is not enabled.");
    public void LoadScriptFromText(string name, string code, string? path = null) => throw new NotSupportedException("Scripting is not enabled.");
    public void UpdateScriptSource(string name, string code) => throw new NotSupportedException("Scripting is not enabled.");
    public void UnloadScript(string name) { }
    public object? Invoke(string scriptName, string entryPoint, object?[]? args = null) => null;
    public void StartLive(string scriptName, SynchronizationContext? uiContext = null) => throw new NotSupportedException("Scripting is not enabled.");
    public void StopLive(string scriptName) { }
    public bool IsScriptLive(string name) => false;
    public string? GetScriptPath(string name) => null;
    public string? GetScriptSource(string name) => null;
}
