using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// A min/max peak summary of an audio source's (mono-summed) samples, bucketed at a fixed
/// resolution. Rendering reads peaks over a frame range rather than the raw samples, so draw cost
/// is independent of file length — the performant basis for waveform display.
///
/// Supports two modes: a finished summary built in one shot from a decoded buffer
/// (<see cref="Build"/>), or a <b>growable</b> summary that is fed blocks via <see cref="Append"/>
/// while a recording fills in. Both expose the same <see cref="GetPeak"/> read contract, so the
/// waveform control draws either without caring which it is.
/// </summary>
public sealed class AudioWaveform
{
    private readonly List<float> _min;
    private readonly List<float> _max;

    // In-progress bucket state, used while growing via Append.
    private float _bucketMin = float.MaxValue;
    private float _bucketMax = float.MinValue;
    private int _inBucket;

    /// <summary>Wraps pre-built peak arrays (finished, immutable-style summary).</summary>
    public AudioWaveform(float[] min, float[] max, int samplesPerBucket, long totalFrames, int sampleRate)
    {
        _min = new List<float>(min);
        _max = new List<float>(max);
        SamplesPerBucket = samplesPerBucket < 1 ? 1 : samplesPerBucket;
        TotalFrames = totalFrames;
        SampleRate = sampleRate;
    }

    /// <summary>Creates an empty, growable summary to be fed via <see cref="Append"/>.</summary>
    public AudioWaveform(int samplesPerBucket, int sampleRate)
    {
        _min = new List<float>();
        _max = new List<float>();
        SamplesPerBucket = samplesPerBucket < 1 ? 1 : samplesPerBucket;
        TotalFrames = 0;
        SampleRate = sampleRate <= 0 ? 44100 : sampleRate;
    }

    /// <summary>Number of source frames summarised by each peak bucket.</summary>
    public int SamplesPerBucket { get; }

    /// <summary>Total number of frames in the source (grows as blocks are appended).</summary>
    public long TotalFrames { get; private set; }

    /// <summary>Source sample rate, in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>Number of completed peak buckets.</summary>
    public int BucketCount => _min.Count;

    /// <summary>Duration of the source, in seconds.</summary>
    public double DurationSeconds => SampleRate > 0 ? (double)TotalFrames / SampleRate : 0.0;

    /// <summary>Builds a mono-summed min/max peak summary from a decoded sample buffer.</summary>
    public static AudioWaveform Build(AudioSampleBuffer buffer, int samplesPerBucket = 128)
    {
        var waveform = new AudioWaveform(samplesPerBucket, buffer.SampleRate);
        waveform.Append(buffer.Samples, buffer.Channels);
        waveform.Flush();
        return waveform;
    }

    /// <summary>
    /// Appends a block of interleaved samples, mono-summing and bucketing them. Safe to call
    /// repeatedly as a recording grows; the trailing partial bucket is held back until the next
    /// block (or <see cref="Flush"/>) so peaks stay accurate.
    /// </summary>
    public void Append(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels < 1) channels = 1;
        var frames = interleaved.Length / channels;

        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            var baseIndex = f * channels;
            for (var c = 0; c < channels; c++) sum += interleaved[baseIndex + c];
            var mono = sum / channels;

            if (mono < _bucketMin) _bucketMin = mono;
            if (mono > _bucketMax) _bucketMax = mono;

            if (++_inBucket >= SamplesPerBucket)
            {
                _min.Add(_bucketMin);
                _max.Add(_bucketMax);
                _bucketMin = float.MaxValue;
                _bucketMax = float.MinValue;
                _inBucket = 0;
            }
        }

        TotalFrames += frames;
    }

    /// <summary>Flushes any trailing partial bucket, finishing the summary.</summary>
    public void Flush()
    {
        if (_inBucket <= 0) return;
        _min.Add(_bucketMin == float.MaxValue ? 0f : _bucketMin);
        _max.Add(_bucketMax == float.MinValue ? 0f : _bucketMax);
        _bucketMin = float.MaxValue;
        _bucketMax = float.MinValue;
        _inBucket = 0;
    }

    /// <summary>
    /// Returns the min and max sample value over the frame range [startFrame, endFrame).
    /// Both outputs are 0 when the range is empty or out of bounds.
    /// </summary>
    public void GetPeak(long startFrame, long endFrame, out float min, out float max)
    {
        min = 0f;
        max = 0f;
        if (_min.Count == 0) return;

        var firstBucket = (int)(startFrame / SamplesPerBucket);
        var lastBucket = (int)((endFrame - 1) / SamplesPerBucket);

        if (firstBucket < 0) firstBucket = 0;
        if (lastBucket >= _min.Count) lastBucket = _min.Count - 1;
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
