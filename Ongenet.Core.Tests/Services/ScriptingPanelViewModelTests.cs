using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Ongenet.App.Platform;
using Ongenet.App.ViewModels.Panels;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Scripting;
using Ongenet.Scripting.Editor;
using Ongenet.Scripting.Export;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class ScriptingPanelViewModelTests
{
    [Fact]
    public void Run_FlushesUnsavedEditorTextBeforeInvoke()
    {
        var instruments = new InstrumentRegistry();
        var project = new ProjectService(instruments);
        var transport = new TransportService();
        var api = new ScriptingApi(project, transport, new CaptureHistory(), new EventAggregator(), instruments, new EffectRegistry(), new ProjectScriptExporter(), new PresetScriptExporter());
        var host = new RoslynScriptingHost(api);
        var dir = Path.Combine(Path.GetTempPath(), $"ongenet-vm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "FlushTest.cs");
        File.WriteAllText(path, "api.SetTempo(120.0);");
        host.LoadScript(path);

        var factory = new TestScriptEditorFactory();
        var vm = new ScriptingPanelViewModel(host, api, factory);
        vm.SelectedScript = vm.Scripts.First(s => s.Name == "FlushTest");
        factory.Session.LoadText("api.SetTempo(180.0);");

        vm.RunCommand.Execute(null);
        for (var i = 0; i < 200 && vm.IsRunning; i++)
            Thread.Sleep(10);

        Assert.Equal(180.0, project.Current.Tempo.BeatsPerMinute, 3);
        Assert.Equal("api.SetTempo(180.0);", host.GetScriptSource("FlushTest"));

        try { Directory.Delete(dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void SelectedScript_SwitchingScripts_LoadsDistinctSourceIntoEditor()
    {
        var instruments = new InstrumentRegistry();
        var api = new ScriptingApi(new ProjectService(instruments), new TransportService(), new CaptureHistory(), new EventAggregator(), instruments, new EffectRegistry());
        var host = new RoslynScriptingHost(api);
        var dir = Path.Combine(Path.GetTempPath(), $"ongenet-switch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var pathA = Path.Combine(dir, "ScriptA.cs");
        var pathB = Path.Combine(dir, "ScriptB.cs");
        File.WriteAllText(pathA, "api.SetTempo(111.0);");
        File.WriteAllText(pathB, "api.SetTempo(222.0);");
        host.LoadScript(pathA);
        host.LoadScript(pathB);

        var factory = new TestScriptEditorFactory();
        var vm = new ScriptingPanelViewModel(host, api, factory);

        vm.SelectedScript = vm.Scripts.First(s => s.Name == "ScriptA");
        Assert.Equal("api.SetTempo(111.0);", vm.EditorText);
        Assert.Equal("api.SetTempo(111.0);", factory.Session.Text);

        vm.SelectedScript = vm.Scripts.First(s => s.Name == "ScriptB");
        Assert.Equal("api.SetTempo(222.0);", vm.EditorText);
        Assert.Equal("api.SetTempo(222.0);", factory.Session.Text);

        try { Directory.Delete(dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void ExportProjectCommand_LoadsGeneratedScriptIntoEditor()
    {
        var instruments = new InstrumentRegistry();
        var api = new ScriptingApi(new ProjectService(instruments), new TransportService(), new CaptureHistory(), new EventAggregator(), instruments, new EffectRegistry(), new ProjectScriptExporter(), new PresetScriptExporter());
        var host = new RoslynScriptingHost(api);
        var factory = new TestScriptEditorFactory();
        var vm = new ScriptingPanelViewModel(host, api, factory);

        vm.ExportProjectCommand.Execute(null);

        Assert.Contains("Generated_Project_", vm.SelectedScript?.Name);
        Assert.Contains("api.ClearProject()", vm.EditorText);
    }

    private sealed class TestScriptEditorFactory : IScriptEditorFactory
    {
        public TestScriptEditorFactory() => Session = new TestScriptEditorSession();

        public TestScriptEditorSession Session { get; }
        public bool IsAvailable => true;

        public IScriptEditorSession CreateSession() => Session;

        public Control CreateEditor(IScriptEditorSession session) => new TextBox();
    }

    private sealed class TestScriptEditorSession : IScriptEditorSession
    {
        public event Action<string>? TextChanged;
        public event Action? AnalysisUpdated { add { } remove { } }

        public string Text { get; set; } = string.Empty;
        public int CaretOffset { get; set; }
        public int ErrorCount => 0;
        public int WarningCount => 0;
        public IReadOnlyList<ScriptCompletionItem> Completions => Array.Empty<ScriptCompletionItem>();
        public IReadOnlyList<ScriptHighlightSpan> HighlightSpans => Array.Empty<ScriptHighlightSpan>();
        public IReadOnlyList<ScriptDiagnosticInfo> Diagnostics => Array.Empty<ScriptDiagnosticInfo>();
        public ScriptSignatureInfo? SignatureHelp => null;

        public void LoadText(string text)
        {
            Text = text;
            TextChanged?.Invoke(text);
        }

        public void ShowCompletion() { }
        public bool TryApplyCompletion(ScriptCompletionItem item) => false;
        public void RefreshAnalysis() { }
        public void Dispose() { }
    }

    private sealed class CaptureHistory : IHistoryCapture
    {
        public void Capture(string label) { }
    }
}
