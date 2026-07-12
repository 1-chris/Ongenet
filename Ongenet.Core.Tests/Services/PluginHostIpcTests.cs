using System;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Services;

public sealed class PluginHostIpcTests
{
    [Fact]
    public async Task Ping_Pong_RoundTrip()
    {
        var pipeName = PluginHostIpc.CreatePipeName(PluginHostIpc.NewInstanceId());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var server = PluginHostIpc.Server.CreateListening(pipeName);
        var acceptTask = server.WaitForClientAsync(cts.Token);

        using var client = await PluginHostIpc.Client.ConnectAsync(pipeName, TimeSpan.FromSeconds(5), cts.Token);
        await acceptTask;

        var serverTask = server.RunAsync((type, _) => Task.FromResult<(PluginHostIpc.MessageType, byte[])?>(
            type == PluginHostIpc.MessageType.Ping
                ? (PluginHostIpc.MessageType.Pong, Array.Empty<byte>())
                : null), cts.Token);

        await client.PingAsync(cts.Token);
        await client.SendShutdownAsync(cts.Token);
        await serverTask;
    }

    [Fact]
    public async Task ProcessAudio_EchoesSamples()
    {
        var pipeName = PluginHostIpc.CreatePipeName(PluginHostIpc.NewInstanceId());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var server = PluginHostIpc.Server.CreateListening(pipeName);
        var acceptTask = server.WaitForClientAsync(cts.Token);

        using var client = await PluginHostIpc.Client.ConnectAsync(pipeName, TimeSpan.FromSeconds(5), cts.Token);
        await acceptTask;

        var serverTask = server.RunAsync((type, payload) =>
        {
            if (type != PluginHostIpc.MessageType.ProcessAudio)
                return Task.FromResult<(PluginHostIpc.MessageType, byte[])?>(null);

            var decoded = PluginHostIpc.DecodeProcessAudio(payload);
            var reply = PluginHostIpc.EncodeProcessAudio(decoded.SampleRate, decoded.Channels, decoded.FrameCount,
                decoded.Samples);
            return Task.FromResult<(PluginHostIpc.MessageType, byte[])?>(
                (PluginHostIpc.MessageType.ProcessAudioResult, reply));
        }, cts.Token);

        var input = new[] { 0.1f, -0.2f, 0.3f, -0.4f };
        var output = await client.ProcessAudioAsync(44100, 2, 2, input, cts.Token);

        Assert.Equal(input, output);
        await client.SendShutdownAsync(cts.Token);
        await serverTask;
    }

    [Fact]
    public async Task LoadPlugin_ReturnsSuccessFlag()
    {
        var pipeName = PluginHostIpc.CreatePipeName(PluginHostIpc.NewInstanceId());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var server = PluginHostIpc.Server.CreateListening(pipeName);
        var acceptTask = server.WaitForClientAsync(cts.Token);

        using var client = await PluginHostIpc.Client.ConnectAsync(pipeName, TimeSpan.FromSeconds(5), cts.Token);
        await acceptTask;

        var serverTask = server.RunAsync((type, payload) =>
        {
            if (type != PluginHostIpc.MessageType.LoadPlugin)
                return Task.FromResult<(PluginHostIpc.MessageType, byte[])?>(null);

            var (path, uid, name) = PluginHostIpc.DecodeLoadPlugin(payload);
            var ok = path.Contains("test.vst3", StringComparison.OrdinalIgnoreCase)
                     && uid == "abc"
                     && name == "Test";
            return Task.FromResult<(PluginHostIpc.MessageType, byte[])?>(
                (PluginHostIpc.MessageType.LoadPluginResult, PluginHostIpc.EncodeLoadPluginResult(ok)));
        }, cts.Token);

        var (success, _) = await client.LoadPluginAsync("/plugins/test.vst3", "abc", "Test", cts.Token);

        Assert.True(success);
        await client.SendShutdownAsync(cts.Token);
        await serverTask;
    }

    [Fact]
    public void EncodeDecode_SetParameter_RoundTrip()
    {
        var payload = PluginHostIpc.EncodeSetParameter(42, 0.75);
        var (id, value) = PluginHostIpc.DecodeSetParameter(payload);
        Assert.Equal(42u, id);
        Assert.Equal(0.75, value, 3);
    }

    [Fact]
    public void EncodeDecode_Latency_RoundTrip()
    {
        var payload = PluginHostIpc.EncodeLatency(512);
        Assert.Equal(512, PluginHostIpc.DecodeLatency(payload));
    }
}
