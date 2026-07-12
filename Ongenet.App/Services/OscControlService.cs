using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Events;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.App.Services;

/// <summary>
/// Lightweight OSC bridge for hardware CV/OSC controllers. Listens for <c>/ongenet/track/&lt;index&gt;/volume</c>,
/// <c>/ongenet/track/&lt;index&gt;/pan</c>, and <c>/ongenet/transport/play</c> messages and maps them to project state.
/// </summary>
public sealed class OscControlService : IDisposable
{
    private readonly IProjectService _project;
    private readonly ITransportService _transport;
    private readonly IEventAggregator _events;
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private int _port = 9000;

    public OscControlService(IProjectService project, ITransportService transport, IEventAggregator events)
    {
        _project = project;
        _transport = transport;
        _events = events;
    }

    public bool IsRunning { get; private set; }
    public int Port => _port;

    public void Start(int port = 9000)
    {
        Stop();
        _port = port;
        try
        {
            _client = new UdpClient(port);
            _cts = new CancellationTokenSource();
            IsRunning = true;
            _ = Task.Run(() => ReceiveLoop(_cts.Token));
        }
        catch
        {
            Stop();
        }
    }

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _client?.Dispose();
        _client = null;
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _client is { } client)
        {
            try
            {
                var result = await client.ReceiveAsync(token);
                HandleMessage(Encoding.UTF8.GetString(result.Buffer));
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore malformed packets */ }
        }
    }

    internal void HandleMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var lines = message.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return;
        var address = lines[0];
        var args = ParseArgs(lines.AsSpan(1));

        if (address.StartsWith("/ongenet/track/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = address["/ongenet/track/".Length..];
            var slash = rest.IndexOf('/');
            if (slash <= 0 || args.Count == 0) return;
            if (!int.TryParse(rest[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) return;
            var target = rest[(slash + 1)..].ToLowerInvariant();
            ApplyTrack(index - 1, target, args[0]);
            return;
        }

        if (string.Equals(address, "/ongenet/transport/play", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count > 0 && args[0] >= 0.5)
            {
                if (_transport.State != TransportState.Playing) _transport.Play();
            }
            else _transport.Stop();
        }
        else if (string.Equals(address, "/ongenet/transport/stop", StringComparison.OrdinalIgnoreCase))
        {
            _transport.Stop();
        }
    }

    private void ApplyTrack(int index, string target, float value)
    {
        var tracks = _project.Current.Tracks.Where(t => !t.IsBus).ToList();
        if (index < 0 || index >= tracks.Count) return;
        var track = tracks[index];
        switch (target)
        {
            case "volume":
                track.Volume = Math.Clamp(value, 0f, 1f);
                break;
            case "pan":
                track.Pan = Math.Clamp(value * 2f - 1f, -1f, 1f);
                break;
            default:
                return;
        }

        _events.Publish(new TrackChangedEvent(track));
    }

    private static List<float> ParseArgs(ReadOnlySpan<string> lines)
    {
        var args = new List<float>();
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            if (line[0] == ',')
            {
                var typeTag = line[1..];
                var payload = typeTag.Length > 1 ? typeTag[1..] : "";
                if (typeTag.StartsWith('f') && float.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    args.Add(f);
            }
            else if (float.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                args.Add(v);
            }
        }

        return args;
    }

    public void Dispose() => Stop();
}
