using System;
using System.Collections.Generic;
using System.IO;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.VideoComposition.Rendering;

/// <summary>Feeds offline stem PCM into visualiser capture at a moving export timeline position.</summary>
public sealed class OfflineVideoAudioScope : IVideoAudioScopeService
{
    private readonly IReadOnlyDictionary<Guid, AudioSampleBuffer> _stems;
    private double _timeSeconds;

    public OfflineVideoAudioScope(IReadOnlyDictionary<Guid, AudioSampleBuffer> stems) => _stems = stems;

    public void SetTime(double timeSeconds) => _timeSeconds = timeSeconds;

    public void BeginBlock() { }

    public void Request(Guid trackId) { }

    public bool IsRequested(Guid trackId) => trackId != Guid.Empty && _stems.ContainsKey(trackId);

    public void Tap(Guid trackId, ReadOnlySpan<float> interleaved, int channels, int sampleRate) { }

    public int CaptureLatest(Guid trackId, float[] dest)
    {
        if (trackId == Guid.Empty || dest.Length == 0) return 0;
        if (!_stems.TryGetValue(trackId, out var buffer)) return 0;

        var endFrame = (long)(_timeSeconds * buffer.SampleRate);
        if (endFrame > buffer.FrameCount) endFrame = buffer.FrameCount;
        if (endFrame <= 0)
        {
            if (buffer.FrameCount <= 0) return 0;
            endFrame = 1;
        }

        var startFrame = Math.Max(0, endFrame - dest.Length);
        var count = (int)(endFrame - startFrame);
        var channels = Math.Max(1, buffer.Channels);
        for (var i = 0; i < count; i++)
        {
            var frame = startFrame + i;
            var mono = 0f;
            for (var ch = 0; ch < channels; ch++)
                mono += buffer.Sample(frame, ch);
            dest[i] = mono / channels;
        }

        return count;
    }

    public int GetSampleRate(Guid trackId) =>
        _stems.TryGetValue(trackId, out var buffer) ? buffer.SampleRate : 48000;
}
