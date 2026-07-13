using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services;

public sealed class NullLiveVideoDecoder : ILiveVideoDecoder
{
    public bool IsRunning => false;
    public int Width => 0;
    public int Height => 0;

    public bool Open(string videoPath, double startSeconds, int width = 1280, int height = 720) => false;
    public byte[]? ReadFrame() => null;
    public void Seek(string videoPath, double seconds) { }
    public void Close() { }
    public void Dispose() { }
}

public sealed class NullVideoFrameExtractor : IVideoFrameExtractor
{
    public bool IsAvailable => false;
    public byte[]? ExtractFramePng(string videoPath, double timeSeconds) => null;
}

public sealed class NullVideoCompositor : IVideoCompositor
{
    public bool IsAvailable => false;

    public void Export(Project project, string wavPath, string outputPath, double durationSeconds,
        IReadOnlyDictionary<Guid, double>? layerOpacities = null,
        IVideoWaveformCacheService? waveformCache = null, double bpm = 120,
        double startBeat = 0, IProgress<double>? progress = null) =>
        throw new NotSupportedException("Video compositing is not available on this platform.");
}

public sealed class NullVideoMuxer : IVideoMuxer
{
    public bool IsAvailable => false;

    public void Mux(string wavPath, string videoPath, double videoOffsetSeconds, string outputPath,
        double inPointSeconds = 0, double outPointSeconds = 0) =>
        throw new NotSupportedException("Video muxing is not available on this platform.");
}

public sealed class NullVideoWaveformCacheService : IVideoWaveformCacheService
{
    public int Revision => 0;
    public void Invalidate() { }
    public AudioWaveform? TryGet(Guid trackId) => null;
    public AudioWaveform GetOrBuild(Project project, Guid trackId, double bpm, IProgress<double>? progress = null) =>
        throw new NotSupportedException("Video waveform cache is not available on this platform.");
    public AudioSampleBuffer GetOrBuildStemBuffer(Project project, Guid trackId, double bpm, IProgress<double>? progress = null) =>
        throw new NotSupportedException("Video waveform cache is not available on this platform.");
    public string GetOrBuildStemWavPath(Project project, Guid trackId, double bpm, IProgress<double>? progress = null) =>
        throw new NotSupportedException("Video waveform cache is not available on this platform.");
}

public sealed class NullVideoAudioScopeService : IVideoAudioScopeService
{
    public void BeginBlock() { }
    public void Request(Guid trackId) { }
    public bool IsRequested(Guid trackId) => false;
    public void Tap(Guid trackId, ReadOnlySpan<float> interleaved, int channels, int sampleRate) { }
    public int CaptureLatest(Guid trackId, float[] dest) => 0;
    public int GetSampleRate(Guid trackId) => 48000;
}
