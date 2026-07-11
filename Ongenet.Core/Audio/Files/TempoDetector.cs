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
    /// Estimates tempo from the audio using the Queen Mary beat tracker (Mixxx default).
    /// When <paramref name="hintBpm"/> is supplied, analysis is biased toward that tempo —
    /// useful when re-analyzing after a pitch shift that should not change BPM.
    /// Returns null for material too short or too flat to judge.
    /// </summary>
    public static double? Estimate(AudioSampleBuffer buffer, double? hintBpm = null)
        => QueenMaryTempoDetector.Detect(buffer, hintBpm);
}
