using System;
using System.Collections.Generic;
using System.Linq;

namespace Ongenet.Core.Audio.Files;

/// <summary>How beat slices are detected in the Audio Editor.</summary>
public enum BeatSliceDetectMode
{
    Transients,
    EqualDivisions
}

/// <summary>Beat-slice grid detection and region management (SliceX-style).</summary>
public static class BeatSliceOps
{
    public static void SliceToGrid(AudioSampleBuffer buffer, BeatSliceDetectMode mode, double secondsPerBeat,
        int divisionsPerBeat = 4, double transientSensitivity = 0.35, double minGapSeconds = 0.05)
    {
        if (buffer.FrameCount <= 0 || secondsPerBeat <= 0) return;

        var markers = mode switch
        {
            BeatSliceDetectMode.Transients => DetectTransientFrames(buffer, transientSensitivity, minGapSeconds),
            _ => DetectEqualDivisionFrames(buffer, secondsPerBeat, divisionsPerBeat)
        };

        buffer.SliceRegions.Clear();
        buffer.SliceRegions.AddRange(BuildRegions(buffer, markers));
    }

    public static IReadOnlyList<long> DetectTransientFrames(AudioSampleBuffer buffer, double sensitivity,
        double minGapSeconds)
    {
        if (buffer.FrameCount <= 0) return Array.Empty<long>();

        var mono = SampleMixdown.ToMono(buffer, guard: false);
        var sampleRate = buffer.SampleRate;
        const int frameLength = 2048;
        const int hop = 512;
        var detector = new QueenMaryDetectionFunction(frameLength);

        var strengths = new List<double>();
        for (var pos = 0; pos + frameLength <= mono.Length; pos += hop)
            strengths.Add(detector.ProcessTimeDomain(mono.AsSpan(pos, frameLength)));

        if (strengths.Count == 0) return new List<long> { 0 };

        var peak = 0.0;
        foreach (var s in strengths)
            if (s > peak) peak = s;

        var threshold = peak * Math.Clamp(sensitivity, 0.05, 0.95);
        var minGapFrames = (long)Math.Round(Math.Max(0.01, minGapSeconds) * sampleRate);
        var peaks = new List<long> { 0 };
        long lastPeak = 0;

        for (var i = 1; i < strengths.Count - 1; i++)
        {
            var s = strengths[i];
            if (s < threshold || s < strengths[i - 1] || s < strengths[i + 1]) continue;
            var frame = (long)i * hop;
            if (frame - lastPeak < minGapFrames) continue;
            peaks.Add(frame);
            lastPeak = frame;
        }

        return peaks;
    }

    public static IReadOnlyList<long> DetectEqualDivisionFrames(AudioSampleBuffer buffer, double secondsPerBeat,
        int divisionsPerBeat)
    {
        if (buffer.FrameCount <= 0 || secondsPerBeat <= 0) return new List<long> { 0 };

        divisionsPerBeat = Math.Clamp(divisionsPerBeat, 1, 64);
        var sampleRate = buffer.SampleRate;
        var sliceFrames = (long)Math.Round(secondsPerBeat / divisionsPerBeat * sampleRate);
        if (sliceFrames <= 0) return new List<long> { 0 };

        var frames = new List<long> { 0 };
        for (var f = sliceFrames; f < buffer.FrameCount; f += sliceFrames)
            frames.Add(f);
        return frames;
    }

    public static IReadOnlyList<AudioSliceRegion> BuildRegions(AudioSampleBuffer buffer, IReadOnlyList<long> markers)
    {
        if (buffer.FrameCount <= 0) return Array.Empty<AudioSliceRegion>();

        var ordered = markers.OrderBy(m => m).Distinct().ToList();
        if (ordered.Count == 0 || ordered[0] != 0) ordered.Insert(0, 0);

        var regions = new List<AudioSliceRegion>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var start = Math.Clamp(ordered[i], 0, buffer.FrameCount);
            var end = i + 1 < ordered.Count
                ? Math.Clamp(ordered[i + 1], start, buffer.FrameCount)
                : buffer.FrameCount;
            if (end <= start) continue;
            regions.Add(new AudioSliceRegion
            {
                StartFrame = start,
                EndFrame = end,
                Order = regions.Count,
                Selected = true
            });
        }

        return regions;
    }

    public static IReadOnlyList<AudioSliceRegion> OrderedRegions(AudioSampleBuffer buffer)
        => buffer.SliceRegions.OrderBy(r => r.Order).ToArray();

    public static void ReorderRegion(AudioSampleBuffer buffer, int fromIndex, int toIndex)
    {
        var ordered = OrderedRegions(buffer).ToList();
        if (fromIndex < 0 || toIndex < 0 || fromIndex >= ordered.Count || toIndex >= ordered.Count) return;
        var item = ordered[fromIndex];
        ordered.RemoveAt(fromIndex);
        ordered.Insert(toIndex, item);
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;
    }

    public static void MoveRegionUp(AudioSampleBuffer buffer, int index)
    {
        if (index <= 0) return;
        ReorderRegion(buffer, index, index - 1);
    }

    public static void MoveRegionDown(AudioSampleBuffer buffer, int index)
    {
        var count = buffer.SliceRegions.Count;
        if (index < 0 || index >= count - 1) return;
        ReorderRegion(buffer, index, index + 1);
    }

    public static AudioSampleBuffer RebuildBufferInSliceOrder(AudioSampleBuffer buffer)
    {
        var ordered = OrderedRegions(buffer);
        if (ordered.Count == 0) return buffer;

        var parts = new List<AudioSegment>(ordered.Count);
        foreach (var region in ordered)
        {
            var count = region.EndFrame - region.StartFrame;
            if (count <= 0) continue;
            parts.Add(SampleEditOps.CopyRange(buffer, region.StartFrame, count));
        }

        return ConcatSegments(parts, buffer.Channels, buffer.SampleRate);
    }

    public static AudioSampleBuffer ConcatSegments(IReadOnlyList<AudioSegment> parts, int channels, int sampleRate)
    {
        if (parts.Count == 0)
            return new AudioSampleBuffer(Array.Empty<float>(), channels, sampleRate);

        long total = 0;
        foreach (var part in parts) total += part.FrameCount;
        var merged = new float[total * channels];
        long offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part.Samples, 0, merged, offset * channels, part.Samples.Length);
            offset += part.FrameCount;
        }

        return new AudioSampleBuffer(merged, channels, sampleRate);
    }

    public static void CopySliceRegions(AudioSampleBuffer from, AudioSampleBuffer to)
    {
        to.SliceRegions.Clear();
        foreach (var region in from.SliceRegions)
        {
            to.SliceRegions.Add(new AudioSliceRegion
            {
                StartFrame = region.StartFrame,
                EndFrame = region.EndFrame,
                Order = region.Order,
                Selected = region.Selected
            });
        }
    }
}
