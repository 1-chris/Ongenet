using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Tempo estimation modeled on Mixxx's default Queen Mary beat analyzer
/// (<c>AnalyzerQueenMaryBeats</c> / qm-dsp <c>TempoTrackV2</c>).
/// </summary>
internal static class QueenMaryTempoDetector
{
    private const float StepSecs = 0.01161f;
    private const int MaximumBinSizeHz = 50;

    public static double? Detect(AudioSampleBuffer buffer, double? hintBpm = null)
    {
        var sampleRate = buffer.SampleRate;
        var frames = buffer.FrameCount;
        if (sampleRate <= 0 || frames < sampleRate) return null;

        var stepSize = Math.Max(1, (int)Math.Round(sampleRate * StepSecs));
        var windowSize = QueenMaryMath.NextPowerOfTwo(sampleRate / MaximumBinSizeHz);
        if (frames < windowSize + stepSize) return null;

        var mono = Downmix(buffer);
        var detector = new QueenMaryDetectionFunction(windowSize);
        var detection = new List<double>();

        for (var pos = 0; pos + windowSize <= mono.Length; pos += stepSize)
        {
            var slice = mono.AsSpan(pos, windowSize);
            detection.Add(detector.ProcessTimeDomain(slice));
        }

        if (detection.Count < 4) return null;

        var nonZero = detection.Count;
        while (nonZero > 0 && detection[nonZero - 1] <= 0.0) nonZero--;
        if (nonZero <= 2) return null;

        var df = new List<double>(nonZero - 2);
        for (var i = 2; i < nonZero; i++)
            df.Add(detection[i]);

        var constrain = hintBpm is > 0;
        var inputTempo = constrain ? hintBpm!.Value : 120.0;

        var tracker = new QueenMaryTempoTrackV2(sampleRate, stepSize);
        var beatPeriod = new List<int>();
        tracker.CalculateBeatPeriod(df, beatPeriod, inputTempo, constrain);

        var beats = new List<double>();
        tracker.CalculateBeats(df, beatPeriod, beats);

        if (beats.Count >= 2)
        {
            var intervals = new List<double>(beats.Count - 1);
            for (var i = 1; i < beats.Count; i++)
            {
                var seconds = (beats[i] - beats[i - 1]) * stepSize / sampleRate;
                if (seconds > 1e-6) intervals.Add(seconds);
            }

            if (intervals.Count > 0)
            {
                var bpm = 60.0 / QueenMaryMath.Median(intervals);
                return FinalizeBpm(QueenMaryMath.FoldToDanceRange(bpm), hintBpm);
            }
        }

        if (beatPeriod.Count == 0) return hintBpm;
        var periodSum = 0.0;
        var periodCount = 0;
        for (var i = beatPeriod.Count / 4; i < beatPeriod.Count; i++)
        {
            if (beatPeriod[i] <= 0) continue;
            periodSum += beatPeriod[i];
            periodCount++;
        }

        if (periodCount == 0) return hintBpm;
        var avgPeriod = periodSum / periodCount;
        var fallback = tracker.BeatPeriodToBpm((int)Math.Round(avgPeriod));
        return fallback > 0
            ? FinalizeBpm(QueenMaryMath.FoldToDanceRange(fallback), hintBpm)
            : hintBpm;
    }

    /// <summary>
    /// When re-analyzing with a prior tempo hint, snap octave errors and preserve the hint if the
    /// tracker drifts (common after pitch shifting).
    /// </summary>
    private static double? FinalizeBpm(double detected, double? hintBpm)
    {
        if (hintBpm is not > 0) return detected;

        for (var octave = -2; octave <= 2; octave++)
        {
            var scaled = detected * Math.Pow(2.0, octave);
            if (Math.Abs(scaled - hintBpm.Value) / hintBpm.Value <= 0.06)
                return hintBpm.Value;
        }

        if (Math.Abs(detected - hintBpm.Value) / hintBpm.Value > 0.08)
            return hintBpm.Value;

        return detected;
    }

    private static float[] Downmix(AudioSampleBuffer buffer)
    {
        var frames = buffer.FrameCount;
        var channels = buffer.Channels;
        var mono = new float[frames];
        if (channels <= 1)
        {
            Array.Copy(buffer.Samples, mono, frames);
            return mono;
        }

        for (long f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
                sum += buffer.Sample(f, c);
            mono[f] = sum / channels;
        }

        return mono;
    }
}
