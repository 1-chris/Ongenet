namespace Ongenet.Scripting.Editor;

public readonly record struct ScriptDiagnosticInfo(int Start, int Length, string Message, bool IsError);
