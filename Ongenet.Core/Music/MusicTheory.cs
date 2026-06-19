using System;

namespace Ongenet.Core.Music;

/// <summary>The diatonic scales/modes the generator can build chords in.</summary>
public enum ScaleType
{
    Major,
    NaturalMinor,
    Dorian,
    Phrygian,
    Lydian,
    Mixolydian,
    Locrian,
    HarmonicMinor,
    MelodicMinor,
    MajorPentatonic,
    MinorPentatonic
}

/// <summary>
/// Note-name and scale helpers shared by the chord/arp generators. Pitches are MIDI note numbers
/// (60 = middle C). Chords are built by stacking thirds <i>within the scale</i>, which keeps every
/// generated chord diatonic without needing a per-degree quality table.
/// </summary>
public static class MusicTheory
{
    /// <summary>Pitch-class names, index 0 = C.</summary>
    public static readonly string[] NoteNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // Semitone offsets from the tonic for one octave of each scale.
    private static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly int[] NaturalMinor = { 0, 2, 3, 5, 7, 8, 10 };
    private static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
    private static readonly int[] Phrygian = { 0, 1, 3, 5, 7, 8, 10 };
    private static readonly int[] Lydian = { 0, 2, 4, 6, 7, 9, 11 };
    private static readonly int[] Mixolydian = { 0, 2, 4, 5, 7, 9, 10 };
    private static readonly int[] Locrian = { 0, 1, 3, 5, 6, 8, 10 };
    private static readonly int[] HarmonicMinor = { 0, 2, 3, 5, 7, 8, 11 };
    private static readonly int[] MelodicMinor = { 0, 2, 3, 5, 7, 9, 11 };
    private static readonly int[] MajorPentatonic = { 0, 2, 4, 7, 9 };
    private static readonly int[] MinorPentatonic = { 0, 3, 5, 7, 10 };

    /// <summary>Semitone offsets (from the tonic) of one octave of the given scale.</summary>
    public static int[] ScaleIntervals(ScaleType scale) => scale switch
    {
        ScaleType.Major => Major,
        ScaleType.NaturalMinor => NaturalMinor,
        ScaleType.Dorian => Dorian,
        ScaleType.Phrygian => Phrygian,
        ScaleType.Lydian => Lydian,
        ScaleType.Mixolydian => Mixolydian,
        ScaleType.Locrian => Locrian,
        ScaleType.HarmonicMinor => HarmonicMinor,
        ScaleType.MelodicMinor => MelodicMinor,
        ScaleType.MajorPentatonic => MajorPentatonic,
        ScaleType.MinorPentatonic => MinorPentatonic,
        _ => Major
    };

    /// <summary>Human-readable name for a pitch class (0 = C) — friendly display only.</summary>
    public static string PitchClassName(int pitchClass) => NoteNames[((pitchClass % 12) + 12) % 12];

    /// <summary>
    /// MIDI note of the tonic for a key. <paramref name="pitchClass"/> is 0..11 (0 = C);
    /// <paramref name="octave"/> follows the C4 = 60 convention.
    /// </summary>
    public static int KeyRoot(int pitchClass, int octave) => (octave + 1) * 12 + (((pitchClass % 12) + 12) % 12);

    /// <summary>
    /// Builds a diatonic chord by stacking thirds within the scale, starting from
    /// <paramref name="degree"/> (0 = tonic). <paramref name="chordSize"/> is the number of notes
    /// (3 = triad, 4 = seventh). Returns ascending MIDI note numbers.
    /// </summary>
    public static int[] DiatonicChord(int keyRoot, ScaleType scale, int degree, int chordSize)
    {
        var intervals = ScaleIntervals(scale);
        var n = intervals.Length;
        if (chordSize < 1) chordSize = 1;

        var chord = new int[chordSize];
        for (var j = 0; j < chordSize; j++)
        {
            var idx = degree + 2 * j;            // stack thirds: every other scale tone
            var octaveShift = idx / n;           // wrap into higher octaves as we climb
            var within = ((idx % n) + n) % n;
            chord[j] = keyRoot + intervals[within] + 12 * octaveShift;
        }

        return chord;
    }

    /// <summary>Clamps a MIDI note to the valid 0..127 range.</summary>
    public static int ClampNote(int note) => note < 0 ? 0 : note > 127 ? 127 : note;

    /// <summary>
    /// Snaps a (possibly fractional) MIDI note to the nearest note whose pitch class is in the given
    /// key/scale. Used by auto-tune to pull a detected pitch to the closest in-key note.
    /// </summary>
    public static int SnapToScale(double midiFloat, int rootPitchClass, ScaleType scale)
    {
        var intervals = ScaleIntervals(scale);
        var allowed = new bool[12];
        var root = ((rootPitchClass % 12) + 12) % 12;
        foreach (var iv in intervals) allowed[(root + iv) % 12] = true;

        // Pick the in-scale note with the smallest true (fractional) distance to midiFloat — NOT a
        // round-to-semitone-then-search, which can pick a further note when the fractional part
        // straddles a boundary. ±4 semitones covers the widest gap in any supported scale.
        var center = (int)Math.Round(midiFloat);
        var best = center;
        var bestDist = double.MaxValue;
        for (var offset = -4; offset <= 4; offset++)
        {
            var n = center + offset;
            if (n < 0 || n > 127) continue;
            if (!allowed[((n % 12) + 12) % 12]) continue;
            var dist = Math.Abs(n - midiFloat);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = n;
            }
        }

        return ClampNote(best);
    }
}
