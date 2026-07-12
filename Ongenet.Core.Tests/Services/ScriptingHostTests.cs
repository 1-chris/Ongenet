using System;
using System.IO;
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
        var api = new ScriptingApi(project, transport, history, events);
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

    private sealed class CaptureHistory : IHistoryCapture
    {
        public System.Collections.Generic.List<string> Labels { get; } = new();

        public void Capture(string label) => Labels.Add(label);
    }
}
