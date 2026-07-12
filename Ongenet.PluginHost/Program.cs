using System;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Services;
using Ongenet.Vst.Vst3;

namespace Ongenet.PluginHost;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!TryParseArgs(args, out var pipeName, out var modulePath, out var uid, out var displayName))
        {
            Console.Error.WriteLine("Usage: Ongenet.PluginHost --pipe <name> [--plugin <path> --uid <uid> --name <display>]");
            return 1;
        }

        try
        {
            return RunAsync(pipeName, modulePath, uid, displayName, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PluginHost] fatal: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> RunAsync(string pipeName, string? modulePath, string? uid, string? displayName,
        CancellationToken cancellationToken)
    {
        using var server = await PluginHostIpc.Server.WaitForConnectionAsync(pipeName, cancellationToken)
            .ConfigureAwait(false);

        IAudioEffect? effect = null;
        AudioFormat format = AudioFormat.Default;
        var loaded = false;

        await server.RunAsync(async (type, payload) =>
        {
            switch (type)
            {
                case PluginHostIpc.MessageType.Ping:
                    return (PluginHostIpc.MessageType.Pong, Array.Empty<byte>());

                case PluginHostIpc.MessageType.LoadPlugin:
                {
                    var (path, pluginUid, name) = PluginHostIpc.DecodeLoadPlugin(payload);
                    try
                    {
                        (effect as IDisposable)?.Dispose();
                        effect = new Vst3Effect(path, pluginUid, name);
                        if (effect is Vst3Effect vst3)
                            vst3.Prepare(format);
                        loaded = true;
                        return (PluginHostIpc.MessageType.LoadPluginResult,
                            PluginHostIpc.EncodeLoadPluginResult(true));
                    }
                    catch (Exception ex)
                    {
                        loaded = false;
                        effect = null;
                        return (PluginHostIpc.MessageType.LoadPluginResult,
                            PluginHostIpc.EncodeLoadPluginResult(false, ex.Message));
                    }
                }

                case PluginHostIpc.MessageType.ProcessAudio:
                {
                    var (sampleRate, channels, frameCount, samples) = PluginHostIpc.DecodeProcessAudio(payload);
                    format = new AudioFormat(sampleRate, channels);
                    if (effect is null)
                    {
                        Array.Clear(samples);
                        return (PluginHostIpc.MessageType.ProcessAudioResult,
                            PluginHostIpc.EncodeProcessAudio(sampleRate, channels, frameCount, samples));
                    }

                    try
                    {
                        if (effect is Vst3Effect vst3 && !loaded)
                            vst3.Prepare(format);
                        effect.Prepare(format);
                        effect.Process(samples);
                    }
                    catch
                    {
                        Array.Clear(samples);
                    }

                    return (PluginHostIpc.MessageType.ProcessAudioResult,
                        PluginHostIpc.EncodeProcessAudio(sampleRate, channels, frameCount, samples));
                }

                case PluginHostIpc.MessageType.SetParameter:
                {
                    // Parameter routing by VST id is handled inside Vst3PluginBase; proxy exposes an empty list for now.
                    _ = PluginHostIpc.DecodeSetParameter(payload);
                    return (PluginHostIpc.MessageType.SetParameterResult,
                        PluginHostIpc.EncodeSetParameterResult(effect is not null));
                }

                case PluginHostIpc.MessageType.GetLatency:
                {
                    var latency = effect is Vst3Effect vst3 ? vst3.ReportedLatencySamples : 0;
                    return (PluginHostIpc.MessageType.GetLatencyResult, PluginHostIpc.EncodeLatency(latency));
                }

                default:
                    return (PluginHostIpc.MessageType.Error, PluginHostIpc.EncodeError($"Unknown message: {type}"));
            }
        }, cancellationToken).ConfigureAwait(false);

        if (effect is IDisposable disposable)
            disposable.Dispose();

        return 0;
    }

    private static bool TryParseArgs(string[] args, out string pipeName, out string? modulePath, out string? uid,
        out string? displayName)
    {
        pipeName = "";
        modulePath = null;
        uid = null;
        displayName = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe" when i + 1 < args.Length:
                    pipeName = args[++i];
                    break;
                case "--plugin" when i + 1 < args.Length:
                    modulePath = args[++i];
                    break;
                case "--uid" when i + 1 < args.Length:
                    uid = args[++i];
                    break;
                case "--name" when i + 1 < args.Length:
                    displayName = args[++i];
                    break;
            }
        }

        return !string.IsNullOrWhiteSpace(pipeName);
    }
}
