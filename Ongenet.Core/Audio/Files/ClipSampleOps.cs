using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Helpers for reading and extracting the playable PCM window of an audio <see cref="Clip"/>.
/// </summary>
public static class ClipSampleOps
{
    /// <summary>Describes the frame range this clip reads from its source buffer.</summary>
    public readonly record struct PlayableWindow(long StartFrame, long FrameCount, int Channels, int SampleRate);

    /// <summary>Returns the frame range this clip plays from <see cref="Clip.Samples"/>.</summary>
    public static PlayableWindow? GetPlayableWindow(Clip clip)
    {
        if (!clip.IsAudio || clip.Samples is not { } samples || samples.FrameCount <= 0) return null;

        var channels = samples.Channels;
        var sampleRate = samples.SampleRate;
        var totalFrames = samples.FrameCount;
        var fullDuration = totalFrames / (double)sampleRate;

        var windowSeconds = clip.SourceLengthSeconds ?? Math.Max(0.0, fullDuration - clip.SourceOffsetSeconds);
        var startFrame = (long)Math.Round(clip.SourceOffsetSeconds * sampleRate);
        var windowFrames = (long)Math.Round(windowSeconds * sampleRate);

        if (startFrame < 0) startFrame = 0;
        if (windowFrames <= 0 || startFrame >= totalFrames) return null;
        if (startFrame + windowFrames > totalFrames) windowFrames = totalFrames - startFrame;

        return new PlayableWindow(startFrame, windowFrames, channels, sampleRate);
    }

    /// <summary>Copies this clip's playable window into a new buffer and peak summary.</summary>
    public static (AudioSampleBuffer Buffer, AudioWaveform Waveform)? ExtractWindow(Clip clip)
    {
        if (clip.Samples is not { } samples) return null;
        if (GetPlayableWindow(clip) is not { } window) return null;

        var channels = window.Channels;
        var extracted = new float[window.FrameCount * channels];
        var src = samples.Samples;
        var startFrame = window.StartFrame;

        for (long i = 0; i < window.FrameCount; i++)
        {
            var srcBase = (startFrame + i) * channels;
            var dstBase = i * channels;
            for (var c = 0; c < channels; c++) extracted[dstBase + c] = src[srcBase + c];
        }

        var buffer = new AudioSampleBuffer(extracted, channels, window.SampleRate);
        return (buffer, AudioWaveform.Build(buffer));
    }

    /// <summary>
    /// Gives this clip its own copy of its playable window. Other clips sharing the same source buffer
    /// are unaffected. Resets <see cref="Clip.SourceOffsetSeconds"/> to 0 and preserves slice semantics.
    /// </summary>
    public static bool MakeUnique(Clip clip)
    {
        if (!clip.IsAudio) return false;
        var wasWindowed = clip.SourceLengthSeconds is not null;
        if (ExtractWindow(clip) is not ({ } buffer, var waveform)) return false;

        clip.Samples = buffer;
        clip.Waveform = waveform;
        clip.SourceOffsetSeconds = 0;
        clip.SourceLengthSeconds = wasWindowed ? buffer.FrameCount / (double)buffer.SampleRate : null;
        return true;
    }

    /// <summary>Clamps source windows on every clip that shares <paramref name="buffer"/> after a buffer edit.</summary>
    public static void NormalizeSourceWindows(IEnumerable<Clip> clips, AudioSampleBuffer buffer)
    {
        var fullDuration = buffer.FrameCount / (double)buffer.SampleRate;
        foreach (var clip in clips)
        {
            if (!ReferenceEquals(clip.Samples, buffer)) continue;

            if (clip.SourceOffsetSeconds >= fullDuration)
                clip.SourceOffsetSeconds = Math.Max(0.0, fullDuration - 1e-6);

            if (clip.SourceLengthSeconds is { } len)
            {
                var maxLen = Math.Max(0.0, fullDuration - clip.SourceOffsetSeconds);
                if (len > maxLen) clip.SourceLengthSeconds = maxLen;
                if (len <= 0) clip.SourceLengthSeconds = null;
            }

            if (clip.SourceOffsetSeconds <= 1e-9 && clip.SourceLengthSeconds is { } l &&
                Math.Abs(l - fullDuration) < 1e-6)
                clip.SourceLengthSeconds = null;
        }
    }
}
