using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Music;

/// <summary>Parameters for <see cref="ChordProgressionGenerator.Generate"/>.</summary>
public sealed class ChordGenOptions
{
    /// <summary>Key tonic pitch class, 0..11 (0 = C).</summary>
    public int RootPitchClass { get; set; }

    /// <summary>Octave of the tonic (C4 = 60 convention).</summary>
    public int Octave { get; set; } = 4;

    /// <summary>Scale/mode the progression is diatonic to.</summary>
    public ScaleType Scale { get; set; } = ScaleType.Major;

    /// <summary>Number of chords in the progression.</summary>
    public int ChordCount { get; set; } = 4;

    /// <summary>Length of each chord, in beats.</summary>
    public double ChordLengthBeats { get; set; } = 4.0;

    /// <summary>Allow plain triads (3-note chords).</summary>
    public bool AllowTriads { get; set; } = true;

    /// <summary>Allow seventh chords (4-note chords).</summary>
    public bool AllowSevenths { get; set; }

    /// <summary>Randomly invert some chords (rotate the lowest note up an octave).</summary>
    public bool RandomInversions { get; set; }

    /// <summary>Start the progression on the tonic chord (a grounded opening).</summary>
    public bool StartOnTonic { get; set; } = true;

    /// <summary>Optional RNG seed for reproducible output; null = nondeterministic.</summary>
    public int? Seed { get; set; }

    /// <summary>Note velocity, 0..1.</summary>
    public float Velocity { get; set; } = 0.8f;
}

/// <summary>One chord in a generated progression: its timing, scale degree, root and pitches.</summary>
public sealed class GeneratedChord
{
    /// <summary>Clip-relative start, in beats.</summary>
    public double StartBeat { get; init; }

    /// <summary>Duration, in beats.</summary>
    public double LengthBeats { get; init; }

    /// <summary>Scale degree of the chord root (0 = tonic).</summary>
    public int Degree { get; init; }

    /// <summary>MIDI note of the chord root (before any inversion).</summary>
    public int RootMidi { get; init; }

    /// <summary>The chord's MIDI pitches (post-inversion), ascending.</summary>
    public int[] Pitches { get; init; } = Array.Empty<int>();
}

/// <summary>
/// Generates randomized diatonic chord progressions as clip-relative <see cref="MidiNote"/>s,
/// ready to drop into a piano-roll clip. Chords are stacked thirds within the chosen scale (see
/// <see cref="MusicTheory.DiatonicChord"/>), so everything stays in key.
/// </summary>
public static class ChordProgressionGenerator
{
    /// <summary>Generates the progression as flat <see cref="MidiNote"/>s.</summary>
    public static IReadOnlyList<MidiNote> Generate(ChordGenOptions options)
        => Flatten(GenerateChords(options), options.Velocity);

    /// <summary>
    /// Generates the progression as structured chords (timing + degree + pitches), so callers that
    /// need the harmonic context — e.g. <see cref="MelodyGenerator"/> — can build over the same chords.
    /// </summary>
    public static IReadOnlyList<GeneratedChord> GenerateChords(ChordGenOptions options)
    {
        var chords = new List<GeneratedChord>();
        if (options.ChordCount <= 0 || options.ChordLengthBeats <= 0) return chords;

        var rng = options.Seed is { } seed ? new Random(seed) : new Random();
        var keyRoot = MusicTheory.KeyRoot(options.RootPitchClass, options.Octave);
        var degreeCount = MusicTheory.ScaleIntervals(options.Scale).Length;

        // Resolve the set of allowed chord sizes (always fall back to triads).
        var sizes = new List<int>();
        if (options.AllowTriads) sizes.Add(3);
        if (options.AllowSevenths) sizes.Add(4);
        if (sizes.Count == 0) sizes.Add(3);

        for (var i = 0; i < options.ChordCount; i++)
        {
            var degree = i == 0 && options.StartOnTonic ? 0 : rng.Next(degreeCount);
            var size = sizes[rng.Next(sizes.Count)];
            var chord = MusicTheory.DiatonicChord(keyRoot, options.Scale, degree, size);
            var root = chord.Length > 0 ? chord[0] : keyRoot; // root before inversion

            // Optional inversion: lift the lowest note(s) up an octave.
            if (options.RandomInversions && chord.Length > 2 && rng.Next(2) == 0)
            {
                var lift = 1 + rng.Next(chord.Length - 1);
                for (var k = 0; k < lift; k++) chord[k] += 12;
                Array.Sort(chord);
            }

            chords.Add(new GeneratedChord
            {
                StartBeat = i * options.ChordLengthBeats,
                LengthBeats = options.ChordLengthBeats,
                Degree = degree,
                RootMidi = root,
                Pitches = chord
            });
        }

        return chords;
    }

    /// <summary>Flattens structured chords into individual <see cref="MidiNote"/>s.</summary>
    public static IReadOnlyList<MidiNote> Flatten(IReadOnlyList<GeneratedChord> chords, float velocity)
    {
        var notes = new List<MidiNote>();
        foreach (var chord in chords)
        {
            foreach (var pitch in chord.Pitches)
            {
                notes.Add(new MidiNote
                {
                    Note = MusicTheory.ClampNote(pitch),
                    StartBeat = chord.StartBeat,
                    LengthBeats = chord.LengthBeats,
                    Velocity = velocity
                });
            }
        }

        return notes;
    }
}
