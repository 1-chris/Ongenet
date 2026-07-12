using System;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

/// <summary>Shared Roslyn script options for the host and in-app editor workspace.</summary>
public static class ScriptCompileOptions
{
    public static ScriptOptions CreateOptions() =>
        ScriptOptions.Default
            .AddReferences(typeof(IScriptingApi).Assembly, typeof(object).Assembly)
            .AddImports("System", "System.Collections.Generic", "Ongenet.Core.Services");

    public static Type GlobalsType => typeof(RoslynScriptingHost.ScriptGlobals);
}
