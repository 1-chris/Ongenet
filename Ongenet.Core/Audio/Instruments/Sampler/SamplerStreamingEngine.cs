using System;
using System.Collections.Generic;
using System.Threading;

namespace Ongenet.Core.Audio.Instruments.Sampler;

/// <summary>
/// Single background worker that keeps every active <see cref="SamplerStream"/>'s ring buffer topped up from
/// disk, across all SFZ instruments. Streams register once (per voice) and are serviced while active and
/// skipped while idle, so registration is cheap and the audio thread never does file I/O. One shared
/// thread (rather than one per voice/instrument) bounds the cost regardless of how many samplers are loaded.
/// </summary>
public sealed class SamplerStreamingEngine
{
    private static readonly Lazy<SamplerStreamingEngine> Lazy = new(() => new SamplerStreamingEngine());
    public static SamplerStreamingEngine Instance => Lazy.Value;

    private readonly object _lock = new();
    private readonly List<SamplerStream> _streams = new();
    private readonly List<SamplerStream> _scratch = new();
    private Thread? _thread;
    private volatile bool _running;

    private SamplerStreamingEngine() { }

    /// <summary>Registers a voice's stream once; it is serviced only while it has an active request.</summary>
    public void Register(SamplerStream stream)
    {
        lock (_lock)
        {
            if (!_streams.Contains(stream)) _streams.Add(stream);
            if (_thread is null)
            {
                _running = true;
                _thread = new Thread(Loop)
                {
                    IsBackground = true,
                    Name = "SFZ-Streaming",
                    Priority = ThreadPriority.AboveNormal
                };
                _thread.Start();
            }
        }
    }

    /// <summary>Removes a voice's streams (instrument unload) and closes any open file handles.</summary>
    public void Unregister(IEnumerable<SamplerStream> streams)
    {
        lock (_lock)
        {
            foreach (var s in streams)
            {
                _streams.Remove(s);
                s.Close();
            }
        }
    }

    private void Loop()
    {
        while (_running)
        {
            _scratch.Clear();
            lock (_lock) _scratch.AddRange(_streams);

            var anyActive = false;
            foreach (var s in _scratch)
            {
                s.Service();
                if (s.IsServicing) anyActive = true;
            }

            // Spin tighter while streaming, idle back when nothing is playing.
            Thread.Sleep(anyActive ? 2 : 15);
        }
    }
}
