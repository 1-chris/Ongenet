using Avalonia.Controls;
using Ongenet.App.Platform;
using Ongenet.Scripting;
using Ongenet.Scripting.Editor;
using Ongenet.Scripting.Editor.Controls;

namespace Ongenet.Desktop;

public sealed class ScriptEditorFactory : IScriptEditorFactory
{
    public bool IsAvailable => true;

    public IScriptEditorSession CreateSession() => new ScriptEditorSession();

    public Control CreateEditor(IScriptEditorSession session)
    {
        var control = new ScriptEditorControl();
        if (session is ScriptEditorSession concrete)
            control.Session = concrete;
        return control;
    }
}
