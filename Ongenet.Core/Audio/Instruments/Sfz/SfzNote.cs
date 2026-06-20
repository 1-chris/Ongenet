using System;
using System.Globalization;

namespace Ongenet.Core.Audio.Instruments.Sfz;

/// <summary>
/// Parses SFZ key values, which may be a MIDI note number (0..127) or a note name such as
/// <c>c4</c>, <c>c#4</c>, <c>db4</c> (SFZ convention: middle C / MIDI 60 = <c>c4</c>).
/// </summary>
public static class SfzNote
{
    // Semitone offset within an octave for each letter (c=0 .. b=11).
    private static readonly int[] LetterSemitone = { 9, 11, 0, 2, 4, 5, 7 }; // a b c d e f g

    /// <summary>
    /// Parses a key value to a MIDI note number, or returns null if it isn't a valid key.
    /// Accepts plain integers and note names (case-insensitive); the result is not range-clamped.
    /// </summary>
    public static int? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim();

        // Plain integer note number.
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;

        // Note name: letter, optional accidental(s), signed octave (e.g. c#4, db-1, c4).
        var i = 0;
        var c = char.ToLowerInvariant(s[i]);
        if (c < 'a' || c > 'g') return null;
        var semitone = LetterSemitone[c - 'a'];
        i++;

        while (i < s.Length && (s[i] == '#' || s[i] == 'b' || s[i] == 'B'))
        {
            semitone += s[i] == '#' ? 1 : -1;
            i++;
        }

        if (i >= s.Length) return null;
        if (!int.TryParse(s.AsSpan(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var octave)) return null;

        // SFZ/MIDI: c4 = 60, so octave -1 is the bottom (c-1 = 0).
        return (octave + 1) * 12 + semitone;
    }

    /// <summary>Parses a key value, falling back to <paramref name="fallback"/> when it isn't valid.</summary>
    public static int Parse(string? value, int fallback) => Parse(value) ?? fallback;
}
