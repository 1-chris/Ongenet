using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.VideoComposition.Services;

/// <summary>Ring-buffer audio taps for live video visualisers (mirrors sidechain request semantics).</summary>
public sealed class VideoAudioScopeService : IVideoAudioScopeService
{
    private sealed class TrackTapState
    {
        public SpectrumScope Scope = new();
        public int SampleRate = 44100;
    }

    private readonly Dictionary<Guid, TrackTapState> _taps = new();
    private readonly ConcurrentDictionary<Guid, int> _requestedGen = new();
    private int _requestGeneration;

    public void BeginBlock() => Interlocked.Increment(ref _requestGeneration);

    public void Request(Guid trackId)
    {
        if (trackId == Guid.Empty) return;
        _requestedGen[trackId] = Volatile.Read(ref _requestGeneration);
    }

    public bool IsRequested(Guid trackId)
    {
        if (trackId == Guid.Empty) return false;
        var gen = Volatile.Read(ref _requestGeneration);
        return _requestedGen.TryGetValue(trackId, out var stored) && stored == gen;
    }

    public void Tap(Guid trackId, ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        if (trackId == Guid.Empty || interleaved.IsEmpty) return;
        if (!_taps.TryGetValue(trackId, out var tap))
        {
            tap = new TrackTapState();
            _taps[trackId] = tap;
        }

        if (sampleRate > 0) tap.SampleRate = sampleRate;
        tap.Scope.Tap(interleaved, channels < 1 ? 1 : channels);
    }

    public int CaptureLatest(Guid trackId, float[] dest)
    {
        if (trackId == Guid.Empty || dest.Length == 0) return 0;
        return _taps.TryGetValue(trackId, out var tap) ? tap.Scope.CaptureLatest(dest) : 0;
    }

    public int GetSampleRate(Guid trackId) =>
        _taps.TryGetValue(trackId, out var tap) ? tap.SampleRate : 44100;
}
