using System;
using Avalonia.Controls;
using Ongenet.Scripting;

namespace Ongenet.App.Platform;

public interface IScriptEditorFactory
{
    bool IsAvailable { get; }
    IScriptEditorSession CreateSession();
    Control CreateEditor(IScriptEditorSession session);
}

public sealed class NullScriptEditorFactory : IScriptEditorFactory
{
    public bool IsAvailable => false;
    public IScriptEditorSession CreateSession() => throw new NotSupportedException("Script editor is not available.");
    public Control CreateEditor(IScriptEditorSession session) => new TextBox { IsReadOnly = true };
}
