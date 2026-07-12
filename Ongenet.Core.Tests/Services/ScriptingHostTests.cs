using System;
using System.IO;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Scripting;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class ScriptingHostTests
{
    [Fact]
    public void LoadAndInvoke_SimpleScript_ChangesTempo()
    {
        var instruments = new InstrumentRegistry();
        var project = new ProjectService(instruments);
        var transport = new TransportService();
        var events = new EventAggregator();
        var history = new CaptureHistory();
        var api = new ScriptingApi(project, transport, history, events, instruments, new EffectRegistry());
        var host = new RoslynScriptingHost(api);

        var path = Path.Combine(Path.GetTempPath(), $"ongenet-script-{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, "api.SetTempo(140.0);");
        try
        {
            host.LoadScript(path);
            host.Invoke(Path.GetFileNameWithoutExtension(path), "Run");

            Assert.Equal(140.0, project.Current.Tempo.BeatsPerMinute, 3);
            Assert.Equal(140.0, transport.Tempo.BeatsPerMinute, 3);
            Assert.Contains("Change tempo", history.Labels);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadScript_TracksPathForReload()
    {
        var instruments = new InstrumentRegistry();
        var api = new ScriptingApi(new ProjectService(instruments), new TransportService(), new CaptureHistory(), new EventAggregator(), instruments, new EffectRegistry());
        var host = new RoslynScriptingHost(api);

        var path = Path.Combine(Path.GetTempPath(), $"ongenet-script-{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, "api.Log(\"ok\");");
        try
        {
            var name = Path.GetFileNameWithoutExtension(path);
            host.LoadScript(path);
            Assert.Equal(Path.GetFullPath(path), host.GetScriptPath(name));
            Assert.Contains("api.Log", host.GetScriptSource(name));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StartLive_RegistersHandlers()
    {
        var instruments = new InstrumentRegistry();
        var transport = new TransportService();
        var api = new ScriptingApi(new ProjectService(instruments), transport, new CaptureHistory(), new EventAggregator(), instruments, new EffectRegistry());
        var host = new RoslynScriptingHost(api);

        var path = Path.Combine(Path.GetTempPath(), $"ongenet-live-{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, """
            api.OnTransportStateChanged(state => api.Log(state.ToString()));
            """);
        try
        {
            var name = Path.GetFileNameWithoutExtension(path);
            host.LoadScript(path);
            host.StartLive(name, uiContext: null);
            Assert.True(host.IsScriptLive(name));
            Assert.Contains("Stopped", string.Join('\n', api.OutputLines));
            transport.Play();
            Assert.Contains("Playing", string.Join('\n', api.OutputLines));
            host.StopLive(name);
            Assert.False(host.IsScriptLive(name));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadScriptFromText_RegistersInMemorySource()
    {
        var instruments = new InstrumentRegistry();
        var api = new ScriptingApi(new ProjectService(instruments), new TransportService(), new CaptureHistory(), new EventAggregator(), instruments, new EffectRegistry());
        var host = new RoslynScriptingHost(api);

        host.LoadScriptFromText("InMemory", "api.SetTempo(99.0);");
        Assert.Contains("InMemory", host.LoadedScripts);
        Assert.Equal("api.SetTempo(99.0);", host.GetScriptSource("InMemory"));
        Assert.Null(host.GetScriptPath("InMemory"));
    }

    [Fact]
    public void UpdateScriptSource_RecompilesWithoutReloadingFile()
    {
        var instruments = new InstrumentRegistry();
        var project = new ProjectService(instruments);
        var transport = new TransportService();
        var api = new ScriptingApi(project, transport, new CaptureHistory(), new EventAggregator(), instruments, new EffectRegistry());
        var host = new RoslynScriptingHost(api);

        host.LoadScriptFromText("Tempo", "api.SetTempo(100.0);");
        host.UpdateScriptSource("Tempo", "api.SetTempo(150.0);");
        host.Invoke("Tempo", "Run");

        Assert.Equal(150.0, project.Current.Tempo.BeatsPerMinute, 3);
        Assert.Equal("api.SetTempo(150.0);", host.GetScriptSource("Tempo"));
    }

    [Fact]
    public void LoadScript_CompileError_Throws()
    {
        var instruments = new InstrumentRegistry();
        var api = new ScriptingApi(new ProjectService(instruments), new TransportService(), new CaptureHistory(), new EventAggregator(), instruments, new EffectRegistry());
        var host = new RoslynScriptingHost(api);

        var path = Path.Combine(Path.GetTempPath(), $"ongenet-bad-{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, "this is not valid C#");
        try
        {
            Assert.Throws<InvalidOperationException>(() => host.LoadScript(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CaptureHistory : IHistoryCapture
    {
        public System.Collections.Generic.List<string> Labels { get; } = new();
        public void Capture(string label) => Labels.Add(label);
    }
}
