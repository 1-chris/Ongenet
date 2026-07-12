using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;

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
    // Crossover points for the bass/mid/treble peak split (Hz). Butterworth Q = 0.707.
    private const double BassCrossoverHz = 250.0;
    private const double TrebleCrossoverHz = 4000.0;
    private const double CrossoverQ = 0.707;

    private readonly List<float> _min;
    private readonly List<float> _max;
    private readonly List<float>[] _bandMin;
    private readonly List<float>[] _bandMax;

    // In-progress bucket state, used while growing via Append.
    private float _bucketMin = float.MaxValue;
    private float _bucketMax = float.MinValue;
    private readonly float[] _bandBucketMin = new float[3];
    private readonly float[] _bandBucketMax = new float[3];
    private int _inBucket;

    private Biquad _bassLp;
    private Biquad _midHp;
    private Biquad _midLp;
    private Biquad _trebleHp;
    private BiquadCoefficients _bassLpCoeffs;
    private BiquadCoefficients _midHpCoeffs;
    private BiquadCoefficients _midLpCoeffs;
    private BiquadCoefficients _trebleHpCoeffs;

    /// <summary>Wraps pre-built peak arrays (finished, immutable-style summary).</summary>
    public AudioWaveform(float[] min, float[] max, int samplesPerBucket, long totalFrames, int sampleRate)
        : this(min, max, bandMin: null, bandMax: null, samplesPerBucket, totalFrames, sampleRate)
    {
    }

    /// <summary>Wraps pre-built peak arrays, optionally including per-band peaks.</summary>
    public AudioWaveform(float[] min, float[] max, float[][]? bandMin, float[][]? bandMax,
        int samplesPerBucket, long totalFrames, int sampleRate)
    {
        _min = new List<float>(min);
        _max = new List<float>(max);
        _bandMin = InitBandLists(bandMin);
        _bandMax = InitBandLists(bandMax);
        SamplesPerBucket = samplesPerBucket < 1 ? 1 : samplesPerBucket;
        TotalFrames = totalFrames;
        SampleRate = sampleRate;
        ConfigureBandFilters();
    }

    /// <summary>Creates an empty, growable summary to be fed via <see cref="Append"/>.</summary>
    public AudioWaveform(int samplesPerBucket, int sampleRate)
    {
        _min = new List<float>();
        _max = new List<float>();
        _bandMin = InitBandLists(null);
        _bandMax = InitBandLists(null);
        SamplesPerBucket = samplesPerBucket < 1 ? 1 : samplesPerBucket;
        TotalFrames = 0;
        SampleRate = sampleRate <= 0 ? 44100 : sampleRate;
        ConfigureBandFilters();
        ResetBandBucketState();
    }

    private static List<float>[] InitBandLists(float[][]? source)
    {
        var lists = new List<float>[3];
        for (var b = 0; b < 3; b++)
        {
            lists[b] = source is not null && b < source.Length && source[b] is { } row
                ? new List<float>(row)
                : new List<float>();
        }

        return lists;
    }

    private void ConfigureBandFilters()
    {
        var rate = SampleRate > 0 ? SampleRate : 44100;
        _bassLpCoeffs = BiquadCoefficients.Compute(FilterMode.LowPass, BassCrossoverHz, CrossoverQ, rate);
        _midHpCoeffs = BiquadCoefficients.Compute(FilterMode.HighPass, BassCrossoverHz, CrossoverQ, rate);
        _midLpCoeffs = BiquadCoefficients.Compute(FilterMode.LowPass, TrebleCrossoverHz, CrossoverQ, rate);
        _trebleHpCoeffs = BiquadCoefficients.Compute(FilterMode.HighPass, TrebleCrossoverHz, CrossoverQ, rate);
        _bassLp.Reset();
        _midHp.Reset();
        _midLp.Reset();
        _trebleHp.Reset();
    }

    private void ResetBandBucketState()
    {
        for (var b = 0; b < 3; b++)
        {
            _bandBucketMin[b] = float.MaxValue;
            _bandBucketMax[b] = float.MinValue;
        }
    }

    /// <summary>True when per-band peak buckets were built (always for summaries from <see cref="Build"/>).</summary>
    public bool HasBandPeaks => _bandMin[0].Count > 0;

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

            TrackBandPeaks(mono);

            if (++_inBucket >= SamplesPerBucket)
            {
                _min.Add(_bucketMin);
                _max.Add(_bucketMax);
                FlushBandBucket();
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
        FlushBandBucket();
        _bucketMin = float.MaxValue;
        _bucketMax = float.MinValue;
        _inBucket = 0;
    }

    private void TrackBandPeaks(float mono)
    {
        var bass = (float)_bassLp.Process(in _bassLpCoeffs, mono);
        var treble = (float)_trebleHp.Process(in _trebleHpCoeffs, mono);
        var mid = (float)_midLp.Process(in _midLpCoeffs, _midHp.Process(in _midHpCoeffs, mono));

        TrackBandSample(WaveformBand.Bass, bass);
        TrackBandSample(WaveformBand.Mid, mid);
        TrackBandSample(WaveformBand.Treble, treble);
    }

    private void TrackBandSample(WaveformBand band, float sample)
    {
        var i = (int)band;
        if (sample < _bandBucketMin[i]) _bandBucketMin[i] = sample;
        if (sample > _bandBucketMax[i]) _bandBucketMax[i] = sample;
    }

    private void FlushBandBucket()
    {
        for (var b = 0; b < 3; b++)
        {
            _bandMin[b].Add(_bandBucketMin[b] == float.MaxValue ? 0f : _bandBucketMin[b]);
            _bandMax[b].Add(_bandBucketMax[b] == float.MinValue ? 0f : _bandBucketMax[b]);
            _bandBucketMin[b] = float.MaxValue;
            _bandBucketMax[b] = float.MinValue;
        }
    }

    /// <summary>
    /// Returns the min and max sample value over the frame range [startFrame, endFrame).
    /// Both outputs are 0 when the range is empty or out of bounds.
    /// </summary>
    public void GetPeak(long startFrame, long endFrame, out float min, out float max) =>
        QueryPeaks(_min, _max, startFrame, endFrame, out min, out max);

    /// <summary>
    /// Returns the min and max filtered sample value for <paramref name="band"/> over
    /// [startFrame, endFrame). Falls back to the full-band peaks when band data is absent.
    /// </summary>
    public void GetBandPeak(WaveformBand band, long startFrame, long endFrame, out float min, out float max)
    {
        var i = (int)band;
        if ((uint)i >= 3 || _bandMin[i].Count == 0)
        {
            GetPeak(startFrame, endFrame, out min, out max);
            return;
        }

        QueryPeaks(_bandMin[i], _bandMax[i], startFrame, endFrame, out min, out max);
    }

    private void QueryPeaks(List<float> minList, List<float> maxList,
        long startFrame, long endFrame, out float min, out float max)
    {
        min = 0f;
        max = 0f;
        if (minList.Count == 0) return;

        var firstBucket = (int)(startFrame / SamplesPerBucket);
        var lastBucket = (int)((endFrame - 1) / SamplesPerBucket);

        if (firstBucket < 0) firstBucket = 0;
        if (lastBucket >= minList.Count) lastBucket = minList.Count - 1;
        if (lastBucket < firstBucket) lastBucket = firstBucket;

        min = minList[firstBucket];
        max = maxList[firstBucket];
        for (var b = firstBucket + 1; b <= lastBucket; b++)
        {
            if (minList[b] < min) min = minList[b];
            if (maxList[b] > max) max = maxList[b];
        }
    }
}
