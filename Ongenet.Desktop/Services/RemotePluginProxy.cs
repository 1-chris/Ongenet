using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Services;

namespace Ongenet.Desktop.Services;

/// <summary>
/// Effect proxy that runs a VST3 plugin in an isolated child process and forwards audio over named pipes.
/// Falls back to silence when the child crashes or fails to load the plugin.
/// </summary>
public sealed class RemotePluginProxy : IAudioEffect, IDisposable
{
    private readonly string _modulePath;
    private readonly string _uid;
    private readonly string _typeId;
    private readonly object _gate = new();

    private Process? _process;
    private PluginHostIpc.Client? _client;
    private AudioFormat _format = AudioFormat.Default;
    private bool _loadAttempted;
    private bool _loaded;
    private bool _disposed;
    private int _latencySamples;

    public RemotePluginProxy(string modulePath, string uid, string displayName, string typeId)
    {
        _modulePath = modulePath;
        _uid = uid;
        _typeId = typeId;
        Name = displayName;
    }

    public string Name { get; }
    public string TypeId => _typeId;
    public bool Enabled { get; set; } = true;

    public IReadOnlyList<Parameter> Parameters => Array.Empty<Parameter>();

    public void Prepare(AudioFormat format)
    {
        _format = format;
        EnsureConnected();
    }

    public void Process(Span<float> buffer)
    {
        if (!Enabled || buffer.IsEmpty)
            return;

        lock (_gate)
        {
            if (_disposed)
            {
                buffer.Clear();
                return;
            }

            if (!EnsureConnectedUnsafe())
            {
                buffer.Clear();
                return;
            }

            try
            {
                var frameCount = buffer.Length / Math.Max(1, _format.Channels);
                var output = _client!.ProcessAudioAsync(_format.SampleRate, _format.Channels, frameCount, buffer.ToArray())
                    .GetAwaiter().GetResult();
                if (output.Length == buffer.Length)
                    output.AsSpan().CopyTo(buffer);
            }
            catch
            {
                MarkDeadUnsafe();
                buffer.Clear();
            }
        }
    }

    public IAudioEffect Clone()
        => new RemotePluginProxy(_modulePath, _uid, Name, _typeId) { Enabled = Enabled };

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            TearDownUnsafe();
        }
    }

    public int ReportedLatencySamples => _latencySamples;

    private void EnsureConnected()
    {
        lock (_gate)
        {
            EnsureConnectedUnsafe();
        }
    }

    private bool EnsureConnectedUnsafe()
    {
        if (_loaded && _client is not null && _process is { HasExited: false })
            return true;

        if (_loadAttempted && (_client is null || _process is null || _process.HasExited))
            return false;

        _loadAttempted = true;
        try
        {
            var hostPath = OutOfProcessPluginHost.ResolveHostExecutablePath();
            if (hostPath is null || !File.Exists(hostPath))
                return false;

            var pipeName = PluginHostIpc.CreatePipeName(PluginHostIpc.NewInstanceId());
            _process = System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = $"--pipe {pipeName} --plugin \"{_modulePath}\" --uid \"{_uid}\" --name \"{Name}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (_process is null)
                return false;

            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => MarkDeadUnsafe();

            _client = PluginHostIpc.Client.ConnectAsync(pipeName, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            _client.PingAsync().GetAwaiter().GetResult();

            var (success, _) = _client.LoadPluginAsync(_modulePath, _uid, Name).GetAwaiter().GetResult();
            _loaded = success;
            if (_loaded)
                _latencySamples = _client.GetLatencyAsync().GetAwaiter().GetResult();
            return _loaded;
        }
        catch
        {
            TearDownUnsafe();
            return false;
        }
    }

    private void MarkDeadUnsafe()
    {
        _loaded = false;
        TearDownUnsafe();
    }

    private void TearDownUnsafe()
    {
        try
        {
            _client?.SendShutdownAsync().Wait(250);
        }
        catch
        {
            // Best effort.
        }

        _client?.Dispose();
        _client = null;

        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); }
            catch { /* ignore */ }
        }

        _process?.Dispose();
        _process = null;
    }
}
