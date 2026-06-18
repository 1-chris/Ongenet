using System;

namespace Ongenet.Core.Audio;

/// <summary>
/// Sums any number of <see cref="ISampleSource"/> inputs. The input list is swapped as an
/// immutable array so the audio thread reads it lock-free while the UI thread mutates it.
/// </summary>
public sealed class Mixer : ISampleSource
{
    private readonly object _writeLock = new();
    private volatile ISampleSource[] _sources = Array.Empty<ISampleSource>();
    private AudioFormat _format = AudioFormat.Default;

    public void Prepare(AudioFormat format)
    {
        _format = format;
        foreach (var source in _sources)
        {
            source.Prepare(format);
        }
    }

    /// <summary>Adds an input. It is prepared with the current format before going live.</summary>
    public void Add(ISampleSource source)
    {
        source.Prepare(_format);
        lock (_writeLock)
        {
            var next = new ISampleSource[_sources.Length + 1];
            Array.Copy(_sources, next, _sources.Length);
            next[^1] = source;
            _sources = next;
        }
    }

    /// <summary>Removes all inputs.</summary>
    public void Clear()
    {
        lock (_writeLock)
        {
            _sources = Array.Empty<ISampleSource>();
        }
    }

    public void Render(Span<float> buffer)
    {
        // Lock-free read of the current snapshot; sources add into the shared buffer.
        var sources = _sources;
        foreach (var source in sources)
        {
            source.Render(buffer);
        }
    }
}
