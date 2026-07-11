using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Files;

/// <summary>A contiguous slice of interleaved PCM copied from a sample buffer.</summary>
public sealed class AudioSegment
{
    public AudioSegment(float[] samples, int channels, int sampleRate)
    {
        Samples = samples;
        Channels = channels < 1 ? 1 : channels;
        SampleRate = sampleRate <= 0 ? 44100 : sampleRate;
    }

    public float[] Samples { get; }
    public int Channels { get; }
    public int SampleRate { get; }
    public long FrameCount => Samples.Length / Channels;
}

/// <summary>Immutable PCM edit utilities for sample buffers.</summary>
public static class SampleEditOps
{
    public static AudioSegment CopyRange(AudioSampleBuffer buffer, long startFrame, long frameCount)
    {
        var channels = buffer.Channels;
        startFrame = Math.Clamp(startFrame, 0, buffer.FrameCount);
        frameCount = Math.Clamp(frameCount, 0, buffer.FrameCount - startFrame);
        var copied = new float[frameCount * channels];
        Array.Copy(buffer.Samples, startFrame * channels, copied, 0, copied.Length);
        return new AudioSegment(copied, channels, buffer.SampleRate);
    }

    public static AudioSampleBuffer Trim(AudioSampleBuffer buffer, long keepStartFrame, long keepEndFrame)
    {
        if (keepEndFrame < keepStartFrame) (keepStartFrame, keepEndFrame) = (keepEndFrame, keepStartFrame);
        keepStartFrame = Math.Clamp(keepStartFrame, 0, buffer.FrameCount);
        keepEndFrame = Math.Clamp(keepEndFrame, keepStartFrame, buffer.FrameCount);
        return CopyRange(buffer, keepStartFrame, keepEndFrame - keepStartFrame).ToBuffer();
    }

    public static AudioSampleBuffer DeleteRange(AudioSampleBuffer buffer, long startFrame, long frameCount)
    {
        if (frameCount <= 0) return buffer;
        var channels = buffer.Channels;
        startFrame = Math.Clamp(startFrame, 0, buffer.FrameCount);
        frameCount = Math.Clamp(frameCount, 0, buffer.FrameCount - startFrame);
        if (frameCount <= 0) return buffer;

        var before = startFrame;
        var afterStart = startFrame + frameCount;
        var afterCount = buffer.FrameCount - afterStart;
        var result = new float[(before + afterCount) * channels];

        if (before > 0)
            Array.Copy(buffer.Samples, 0, result, 0, before * channels);
        if (afterCount > 0)
            Array.Copy(buffer.Samples, afterStart * channels, result, before * channels, afterCount * channels);

        return new AudioSampleBuffer(result, channels, buffer.SampleRate);
    }

    public static AudioSampleBuffer InsertRange(AudioSampleBuffer buffer, long atFrame, AudioSegment segment)
    {
        if (segment.FrameCount <= 0) return buffer;
        if (segment.Channels != buffer.Channels || segment.SampleRate != buffer.SampleRate)
            throw new ArgumentException("Segment must match buffer channel count and sample rate.");

        var channels = buffer.Channels;
        atFrame = Math.Clamp(atFrame, 0, buffer.FrameCount);
        var insertFrames = segment.FrameCount;
        var result = new float[(buffer.FrameCount + insertFrames) * channels];

        if (atFrame > 0)
            Array.Copy(buffer.Samples, 0, result, 0, atFrame * channels);
        Array.Copy(segment.Samples, 0, result, atFrame * channels, segment.Samples.Length);
        var tailFrames = buffer.FrameCount - atFrame;
        if (tailFrames > 0)
            Array.Copy(buffer.Samples, atFrame * channels, result, (atFrame + insertFrames) * channels,
                tailFrames * channels);

        return new AudioSampleBuffer(result, channels, buffer.SampleRate);
    }

    public static AudioSampleBuffer MoveRange(AudioSampleBuffer buffer, long fromStart, long frameCount, long toStart)
    {
        if (frameCount <= 0) return buffer;
        var segment = CopyRange(buffer, fromStart, frameCount);
        var without = DeleteRange(buffer, fromStart, frameCount);
        var adjustedTo = toStart;
        if (toStart > fromStart) adjustedTo -= frameCount;
        return InsertRange(without, adjustedTo, segment);
    }

    public static AudioSampleBuffer ApplyGainRange(AudioSampleBuffer buffer, long startFrame, long frameCount,
        double gainLinear)
    {
        if (frameCount <= 0 || Math.Abs(gainLinear - 1.0) < 1e-9) return buffer;
        startFrame = Math.Clamp(startFrame, 0, buffer.FrameCount);
        frameCount = Math.Clamp(frameCount, 0, buffer.FrameCount - startFrame);
        if (frameCount <= 0) return buffer;

        var channels = buffer.Channels;
        var result = (float[])buffer.Samples.Clone();
        var end = startFrame + frameCount;
        for (long f = startFrame; f < end; f++)
        {
            var baseIdx = f * channels;
            for (var c = 0; c < channels; c++)
                result[baseIdx + c] *= (float)gainLinear;
        }

        return new AudioSampleBuffer(result, channels, buffer.SampleRate);
    }

    public static AudioSampleBuffer ApplyPanRange(AudioSampleBuffer buffer, long startFrame, long frameCount,
        double pan)
    {
        if (buffer.Channels < 2 || frameCount <= 0) return buffer;
        startFrame = Math.Clamp(startFrame, 0, buffer.FrameCount);
        frameCount = Math.Clamp(frameCount, 0, buffer.FrameCount - startFrame);
        if (frameCount <= 0) return buffer;

        AudioMath.PanGains(pan, out var panL, out var panR);
        var result = (float[])buffer.Samples.Clone();
        var end = startFrame + frameCount;
        for (long f = startFrame; f < end; f++)
        {
            var baseIdx = f * 2;
            result[baseIdx] *= panL;
            result[baseIdx + 1] *= panR;
        }

        return new AudioSampleBuffer(result, 2, buffer.SampleRate);
    }

    public static AudioSampleBuffer SwapChannelsRange(AudioSampleBuffer buffer, long startFrame, long frameCount)
    {
        if (buffer.Channels < 2 || frameCount <= 0) return buffer;
        startFrame = Math.Clamp(startFrame, 0, buffer.FrameCount);
        frameCount = Math.Clamp(frameCount, 0, buffer.FrameCount - startFrame);
        if (frameCount <= 0) return buffer;

        var result = (float[])buffer.Samples.Clone();
        var end = startFrame + frameCount;
        for (long f = startFrame; f < end; f++)
        {
            var baseIdx = f * 2;
            (result[baseIdx], result[baseIdx + 1]) = (result[baseIdx + 1], result[baseIdx]);
        }

        return new AudioSampleBuffer(result, 2, buffer.SampleRate);
    }

    public static AudioSampleBuffer ReverseRange(AudioSampleBuffer buffer, long startFrame, long frameCount)
    {
        if (frameCount <= 1) return buffer;
        startFrame = Math.Clamp(startFrame, 0, buffer.FrameCount);
        frameCount = Math.Clamp(frameCount, 0, buffer.FrameCount - startFrame);
        if (frameCount <= 1) return buffer;

        var channels = buffer.Channels;
        var result = (float[])buffer.Samples.Clone();
        var endFrame = startFrame + frameCount;
        for (long i = 0; i < frameCount; i++)
        {
            var srcFrame = endFrame - 1 - i;
            var dstFrame = startFrame + i;
            var srcBase = srcFrame * channels;
            var dstBase = dstFrame * channels;
            for (var c = 0; c < channels; c++)
                result[dstBase + c] = buffer.Samples[srcBase + c];
        }

        return new AudioSampleBuffer(result, channels, buffer.SampleRate);
    }

    /// <summary>Replaces shared PCM on a clip and rebuilds its waveform peaks.</summary>
    public static void ReplaceClipAudio(Clip clip, AudioSampleBuffer newBuffer)
    {
        clip.Samples = newBuffer;
        clip.Waveform = AudioWaveform.Build(newBuffer);
    }

    /// <summary>
    /// Replaces PCM on every clip sharing <paramref name="oldBuffer"/> and normalizes source windows.
    /// Does not rebuild waveform peaks — call <see cref="AssignSharedWaveform"/> after async build.
    /// </summary>
    public static void ReplaceSharedBufferSamples(IEnumerable<Clip> clips, AudioSampleBuffer oldBuffer,
        AudioSampleBuffer newBuffer)
    {
        foreach (var clip in clips)
        {
            if (!ReferenceEquals(clip.Samples, oldBuffer)) continue;
            clip.Samples = newBuffer;
        }

        ClipSampleOps.NormalizeSourceWindows(clips, newBuffer);
    }

    /// <summary>Assigns a rebuilt peak summary to every clip sharing <paramref name="buffer"/>.</summary>
    public static void AssignSharedWaveform(IEnumerable<Clip> clips, AudioSampleBuffer buffer, AudioWaveform waveform)
    {
        foreach (var clip in clips)
        {
            if (ReferenceEquals(clip.Samples, buffer))
                clip.Waveform = waveform;
        }
    }

    /// <summary>
    /// Replaces PCM on every clip sharing <paramref name="oldBuffer"/> and normalizes source windows.
    /// </summary>
    public static void ReplaceSharedBuffer(IEnumerable<Clip> clips, AudioSampleBuffer oldBuffer,
        AudioSampleBuffer newBuffer)
    {
        var waveform = AudioWaveform.Build(newBuffer);
        ReplaceSharedBufferSamples(clips, oldBuffer, newBuffer);
        AssignSharedWaveform(clips, newBuffer, waveform);
    }

    private static AudioSampleBuffer ToBuffer(this AudioSegment segment)
        => new(segment.Samples, segment.Channels, segment.SampleRate);
}
