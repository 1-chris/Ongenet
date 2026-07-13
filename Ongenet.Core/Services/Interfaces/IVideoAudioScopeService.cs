using System;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Live mono audio tap for video-layer audio visualisers. The engine publishes requested track/bus
/// outputs each block; the UI reads the latest window via <see cref="CaptureLatest"/>.
/// </summary>
public interface IVideoAudioScopeService
{
    void BeginBlock();

    void Request(Guid trackId);

    bool IsRequested(Guid trackId);

    void Tap(Guid trackId, ReadOnlySpan<float> interleaved, int channels, int sampleRate);

    int CaptureLatest(Guid trackId, float[] dest);

    int GetSampleRate(Guid trackId);
}
