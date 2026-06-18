using System;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Works out a sample's natural tempo (BPM). First it looks for an explicit "&lt;n&gt;bpm" tag in the
/// file name and then up to two parent folder names (the common way loop packs label tempo); failing
/// that it estimates the tempo from the audio itself with an onset-flux autocorrelation.
/// </summary>
public static class TempoDetector
{
    // Matches "150bpm", "150 BPM", "92.5bpm" — a number immediately followed by "bpm".
    private static readonly Regex BpmTag =
        new(@"(\d{2,3}(?:\.\d+)?)\s*bpm", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const double MinBpm = 30.0;
    private const double MaxBpm = 400.0;

    /// <summary>
    /// Reads a tagged tempo from the file name, then up to two parent folder names. Returns null if
    /// no "&lt;n&gt;bpm" tag is present.
    /// </summary>
    public static double? FromPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        if (TryParseBpm(Path.GetFileNameWithoutExtension(path), out var fromFile)) return fromFile;

        var dir = Path.GetDirectoryName(path);
        for (var depth = 0; depth < 2 && !string.IsNullOrEmpty(dir); depth++)
        {
            if (TryParseBpm(Path.GetFileName(dir), out var fromFolder)) return fromFolder;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private static bool TryParseBpm(string? text, out double bpm)
    {
        bpm = 0;
        if (string.IsNullOrEmpty(text)) return false;
        var match = BpmTag.Match(text);
        if (!match.Success) return false;
        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out bpm))
            return false;
        return bpm is >= MinBpm and <= MaxBpm;
    }

    /// <summary>
    /// Estimates tempo from the audio: builds a half-wave-rectified energy-flux onset envelope, then
    /// autocorrelates it to find the dominant periodicity, folded into a musical range. Returns null
    /// for material too short or too flat to judge.
    /// </summary>
    public static double? Estimate(AudioSampleBuffer buffer)
    {
        const int win = 1024;
        const int hop = 512;

        var sampleRate = buffer.SampleRate;
        var frames = buffer.FrameCount;
        if (sampleRate <= 0 || frames < sampleRate) return null; // need ~1s

        var numHops = (int)((frames - win) / hop);
        if (numHops < 16) return null;

        // Onset envelope: positive change in short-window energy.
        var env = new float[numHops];
        var prevEnergy = 0f;
        for (var h = 0; h < numHops; h++)
        {
            long start = (long)h * hop;
            var energy = 0f;
            for (var i = 0; i < win; i++)
            {
                var f = start + i;
                var sum = 0f;
                for (var c = 0; c < buffer.Channels; c++) sum += buffer.Sample(f, c);
                var mono = sum / buffer.Channels;
                energy += mono * mono;
            }

            var flux = energy - prevEnergy;
            prevEnergy = energy;
            env[h] = flux > 0 ? flux : 0;
        }

        var envRate = (double)sampleRate / hop; // onset-envelope samples per second

        // Autocorrelate over lags spanning 60..200 BPM and take the strongest period.
        var minLag = Math.Max(1, (int)(60.0 * envRate / 200.0));
        var maxLag = Math.Min(numHops - 1, (int)(60.0 * envRate / 60.0));
        if (maxLag <= minLag) return null;

        var bestScore = 0.0;
        var bestLag = 0;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var sum = 0.0;
            for (var i = 0; i + lag < numHops; i++) sum += env[i] * env[i + lag];
            sum /= numHops - lag; // normalise by overlap so long lags aren't penalised
            if (sum > bestScore)
            {
                bestScore = sum;
                bestLag = lag;
            }
        }

        if (bestLag <= 0 || bestScore <= 0) return null;

        var bpm = 60.0 * envRate / bestLag;
        // Fold into a typical musical range so half/double-time detections normalise.
        while (bpm < 70.0) bpm *= 2.0;
        while (bpm > 160.0) bpm /= 2.0;
        return bpm;
    }
}
