using System;
using System.Globalization;

namespace Ongenet.Core.Music;

/// <summary>Parses and formats keys in the <c>"C maj"</c> / <c>"A min"</c> style used by <see cref="Audio.Files.MusicalKeyDetector"/>.</summary>
public static class MusicalKeyFormat
{
    /// <summary>Formats a pitch class and mode as e.g. <c>"A min"</c>.</summary>
    public static string Format(int rootPitchClass, bool isMinor)
    {
        var pc = ((rootPitchClass % 12) + 12) % 12;
        return $"{MusicTheory.PitchClassName(pc)} {(isMinor ? "min" : "maj")}";
    }

    /// <summary>
    /// Parses a detected key string. Returns false for empty or unrecognised input.
    /// </summary>
    public static bool TryParse(string? text, out int rootPitchClass, out bool isMinor)
    {
        rootPitchClass = 0;
        isMinor = false;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        var name = parts[0];
        var mode = parts[1].ToLowerInvariant();
        isMinor = mode is "min" or "minor" or "m";
        if (!isMinor && mode is not ("maj" or "major")) return false;

        for (var i = 0; i < MusicTheory.NoteNames.Length; i++)
        {
            if (string.Equals(MusicTheory.NoteNames[i], name, StringComparison.OrdinalIgnoreCase))
            {
                rootPitchClass = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>Smallest signed semitone interval from one pitch class to another (−6..+6).</summary>
    public static int ShortestSemitoneDelta(int fromPitchClass, int toPitchClass)
    {
        var delta = ((toPitchClass - fromPitchClass) % 12 + 12) % 12;
        if (delta > 6) delta -= 12;
        return delta;
    }
}
