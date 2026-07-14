using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;

namespace Ongenet.Core.Audio.Files;

/// <summary>Offline beat-grid warp: stretch source regions to a musical grid with Rubber Band.</summary>
public static class BeatGridWarpOps
{
    public sealed record WarpSegment(long SourceStartFrame, long SourceEndFrame, double TargetBeats);

    /// <summary>
    /// Builds warp segments aligned to <paramref name="beatsPerSegment"/> on the tempo grid.
    /// Uses existing slice regions when present; otherwise equal beat-sized source windows.
    /// </summary>
    public static IReadOnlyList<WarpSegment> BuildBeatGridSegments(AudioSampleBuffer buffer, double secondsPerBeat,
        double beatsPerSegment = 1.0)
    {
        if (buffer.FrameCount <= 0 || secondsPerBeat <= 0 || beatsPerSegment <= 0)
            return Array.Empty<WarpSegment>();

        var segments = new List<WarpSegment>();
        var ordered = BeatSliceOps.OrderedRegions(buffer);
        if (ordered.Count > 0)
        {
            foreach (var region in ordered)
            {
                if (region.EndFrame <= region.StartFrame) continue;
                segments.Add(new WarpSegment(region.StartFrame, region.EndFrame, beatsPerSegment));
            }

            return segments;
        }

        var beatFrames = (long)Math.Round(secondsPerBeat * buffer.SampleRate);
        if (beatFrames <= 0) return Array.Empty<WarpSegment>();

        var segmentFrames = Math.Max(1L, (long)Math.Round(beatFrames * beatsPerSegment));
        for (long start = 0; start < buffer.FrameCount; start += segmentFrames)
        {
            var end = Math.Min(buffer.FrameCount, start + segmentFrames);
            if (end <= start) break;
            segments.Add(new WarpSegment(start, end, beatsPerSegment));
        }

        return segments;
    }

    /// <summary>
    /// Stretches each segment to <paramref name="beatsPerSegment"/> beats and concatenates the result.
    /// </summary>
    public static AudioSampleBuffer ApplyBeatGridWarp(AudioSampleBuffer buffer, double secondsPerBeat,
        double beatsPerSegment = 1.0, bool transientSafe = true, IProgress<double>? progress = null)
    {
        if (buffer.FrameCount <= 0 || secondsPerBeat <= 0) return buffer;

        var segments = BuildBeatGridSegments(buffer, secondsPerBeat, beatsPerSegment);
        if (segments.Count == 0) return buffer;

        var targetFramesPerSegment = (long)Math.Round(beatsPerSegment * secondsPerBeat * buffer.SampleRate);
        if (targetFramesPerSegment <= 0) targetFramesPerSegment = 1;

        var parts = new List<AudioSegment>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var srcLen = seg.SourceEndFrame - seg.SourceStartFrame;
            if (srcLen <= 0) continue;

            var copied = SampleEditOps.CopyRange(buffer, seg.SourceStartFrame, srcLen);
            var stretched = StretchToFrameCount(copied, targetFramesPerSegment, transientSafe);
            parts.Add(stretched);
            progress?.Report((i + 1.0) / segments.Count);
        }

        return BeatSliceOps.ConcatSegments(parts, buffer.Channels, buffer.SampleRate);
    }

    public static AudioSegment StretchToFrameCount(AudioSegment segment, long targetFrames, bool transientSafe)
    {
        if (segment.FrameCount <= 0 || targetFrames <= 0) return segment;
        if (segment.FrameCount == targetFrames) return segment;

        var ratio = (double)targetFrames / segment.FrameCount;
        if (!transientSafe)
        {
            var resampled = ResampleLinear(segment.Samples, segment.Channels, segment.FrameCount, targetFrames);
            return new AudioSegment(resampled, segment.Channels, segment.SampleRate);
        }

        var stretched = RubberBandStretcher.TimeStretch(
            segment.Samples, segment.Channels, segment.SampleRate, ratio);
        var outFrames = stretched.Length / segment.Channels;
        if (outFrames != targetFrames)
            stretched = FitInterleaved(stretched, segment.Channels, targetFrames);

        return new AudioSegment(stretched, segment.Channels, segment.SampleRate);
    }

    private static float[] ResampleLinear(float[] input, int channels, long inFrames, long outFrames)
    {
        if (inFrames <= 0 || outFrames <= 0) return Array.Empty<float>();
        if (inFrames == outFrames) return (float[])input.Clone();

        var output = new float[outFrames * channels];
        for (long f = 0; f < outFrames; f++)
        {
            var srcPos = f * (inFrames - 1.0) / Math.Max(1.0, outFrames - 1.0);
            var f0 = (long)Math.Floor(srcPos);
            var frac = (float)(srcPos - f0);
            var f1 = Math.Min(inFrames - 1, f0 + 1);
            var baseOut = f * channels;
            var base0 = f0 * channels;
            var base1 = f1 * channels;
            for (var c = 0; c < channels; c++)
                output[baseOut + c] = input[base0 + c] + (input[base1 + c] - input[base0 + c]) * frac;
        }

        return output;
    }

    private static float[] FitInterleaved(float[] input, int channels, long targetFrames)
    {
        var currentFrames = input.Length / channels;
        if (currentFrames == targetFrames) return input;
        if (currentFrames <= 0) return new float[targetFrames * channels];

        var output = new float[targetFrames * channels];
        if (currentFrames > targetFrames)
        {
            Array.Copy(input, 0, output, 0, output.Length);
            return output;
        }

        Array.Copy(input, 0, output, 0, input.Length);
        var tail = (targetFrames - currentFrames) * channels;
        var fillStart = input.Length - channels;
        if (fillStart < 0) fillStart = 0;
        for (var i = input.Length; i < output.Length; i += channels)
            Array.Copy(input, fillStart, output, i, channels);
        return output;
    }
}
