using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A shared, audio-thread-only registry that lets an effect read another track's output as a trigger
/// signal (the classic kick → bass/lead sidechain). It's deliberately generic — "publish a named signal,
/// read it elsewhere" — so other future features (metering taps, cross-track modulation, vocoders) can
/// reuse it.
///
/// Flow each block: a consumer calls <see cref="Request"/> with the source track's id; the engine, after
/// it has rendered + effected each requested track, calls <see cref="Publish"/> with that track's output;
/// the consumer reads it with <see cref="Read"/>. If the source is processed after the consumer in the
/// block, the consumer reads the previous block's signal (sub-block latency, inaudible for ducking).
/// All access is on the single audio thread, so no locking is needed; buffers are reused to avoid
/// per-block allocation.
/// </summary>
public interface ISidechainBus
{
    /// <summary>Declares that the caller wants <paramref name="trackId"/>'s output published this/next block.</summary>
    void Request(Guid trackId);

    /// <summary>True when some consumer has requested <paramref name="trackId"/> (so the engine should publish it).</summary>
    bool IsRequested(Guid trackId);

    /// <summary>Stores a copy of a track's interleaved output for consumers to read.</summary>
    void Publish(Guid trackId, ReadOnlySpan<float> interleaved, int channels);

    /// <summary>The last published interleaved signal for <paramref name="trackId"/> (empty if none yet).</summary>
    ReadOnlySpan<float> Read(Guid trackId, out int channels);
}

/// <summary>Default <see cref="ISidechainBus"/>. See the interface for the contract.</summary>
public sealed class SidechainBus : ISidechainBus
{
    /// <summary>A no-op bus used as a safe default when no engine bus is wired.</summary>
    public static readonly ISidechainBus Empty = new SidechainBus();

    private sealed class Tap
    {
        public float[] Buffer = Array.Empty<float>();
        public int Length;   // valid interleaved samples
        public int Channels = 1;
    }

    private readonly Dictionary<Guid, Tap> _taps = new();
    private readonly HashSet<Guid> _requested = new();

    public void Request(Guid trackId)
    {
        if (trackId != Guid.Empty) _requested.Add(trackId);
    }

    public bool IsRequested(Guid trackId) => _requested.Contains(trackId);

    public void Publish(Guid trackId, ReadOnlySpan<float> interleaved, int channels)
    {
        if (trackId == Guid.Empty) return;
        if (!_taps.TryGetValue(trackId, out var tap)) { tap = new Tap(); _taps[trackId] = tap; }
        if (tap.Buffer.Length < interleaved.Length) tap.Buffer = new float[interleaved.Length];
        interleaved.CopyTo(tap.Buffer);
        tap.Length = interleaved.Length;
        tap.Channels = channels < 1 ? 1 : channels;
    }

    public ReadOnlySpan<float> Read(Guid trackId, out int channels)
    {
        if (trackId != Guid.Empty && _taps.TryGetValue(trackId, out var tap) && tap.Length > 0)
        {
            channels = tap.Channels;
            return tap.Buffer.AsSpan(0, tap.Length);
        }

        channels = 1;
        return ReadOnlySpan<float>.Empty;
    }
}
