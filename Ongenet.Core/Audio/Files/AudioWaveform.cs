using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// A precomputed min/max peak summary of an audio file's (mono-summed) samples, bucketed at a
/// fixed resolution. Rendering reads peaks over a frame range rather than the raw samples, so
/// draw cost is independent of file length — the performant basis for waveform display and,
/// later, zoomed overviews.
/// </summary>
public sealed class AudioWaveform
{
    private readonly float[] _min;
    private readonly float[] _max;

    public AudioWaveform(float[] min, float[] max, int samplesPerBucket, long totalFrames, int sampleRate)
    {
        _min = min;
        _max = max;
        SamplesPerBucket = samplesPerBucket < 1 ? 1 : samplesPerBucket;
        TotalFrames = totalFrames;
        SampleRate = sampleRate;
    }

    /// <summary>Number of source frames summarised by each peak bucket.</summary>
    public int SamplesPerBucket { get; }

    /// <summary>Total number of frames in the source.</summary>
    public long TotalFrames { get; }

    /// <summary>Source sample rate, in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>Number of peak buckets.</summary>
    public int BucketCount => _min.Length;

    /// <summary>Duration of the source, in seconds.</summary>
    public double DurationSeconds => SampleRate > 0 ? (double)TotalFrames / SampleRate : 0.0;

    /// <summary>Builds a mono-summed min/max peak summary from a decoded sample buffer.</summary>
    public static AudioWaveform Build(AudioSampleBuffer buffer, int samplesPerBucket = 128)
    {
        var channels = buffer.Channels;
        var frames = buffer.FrameCount;
        var min = new List<float>();
        var max = new List<float>();
        float bucketMin = float.MaxValue, bucketMax = float.MinValue;
        var inBucket = 0;

        for (long f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++) sum += buffer.Samples[f * channels + c];
            var mono = sum / channels;

            if (mono < bucketMin) bucketMin = mono;
            if (mono > bucketMax) bucketMax = mono;

            if (++inBucket >= samplesPerBucket)
            {
                min.Add(bucketMin);
                max.Add(bucketMax);
                bucketMin = float.MaxValue;
                bucketMax = float.MinValue;
                inBucket = 0;
            }
        }

        if (inBucket > 0)
        {
            min.Add(bucketMin == float.MaxValue ? 0f : bucketMin);
            max.Add(bucketMax == float.MinValue ? 0f : bucketMax);
        }

        return new AudioWaveform(min.ToArray(), max.ToArray(), samplesPerBucket, frames, buffer.SampleRate);
    }

    /// <summary>
    /// Returns the min and max sample value over the frame range [startFrame, endFrame).
    /// Both outputs are 0 when the range is empty or out of bounds.
    /// </summary>
    public void GetPeak(long startFrame, long endFrame, out float min, out float max)
    {
        min = 0f;
        max = 0f;
        if (_min.Length == 0) return;

        var firstBucket = (int)(startFrame / SamplesPerBucket);
        var lastBucket = (int)((endFrame - 1) / SamplesPerBucket);

        if (firstBucket < 0) firstBucket = 0;
        if (lastBucket >= _min.Length) lastBucket = _min.Length - 1;
        if (lastBucket < firstBucket) lastBucket = firstBucket;

        min = _min[firstBucket];
        max = _max[firstBucket];
        for (var b = firstBucket + 1; b <= lastBucket; b++)
        {
            if (_min[b] < min) min = _min[b];
            if (_max[b] > max) max = _max[b];
        }
    }
}
