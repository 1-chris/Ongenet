using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Music;

/// <summary>Parameters for <see cref="MelodyGenerator.Generate"/>.</summary>
public sealed class MelodyOptions
{
    /// <summary>Rhythmic grid of the melody, in beats (e.g. 0.5 = 1/8 in 4/4).</summary>
    public double StepBeats { get; set; } = 0.5;

    /// <summary>Octaves above the chords the melody sits in (1..4).</summary>
    public int OctaveOffset { get; set; } = 1;

    /// <summary>Probability that a weak step sounds a note rather than resting, 0..1.</summary>
    public double Density { get; set; } = 0.75;

    /// <summary>Note length as a fraction of the step, 0..1.</summary>
    public double Gate { get; set; } = 0.9;

    /// <summary>Note velocity, 0..1.</summary>
    public float Velocity { get; set; } = 0.85f;

    /// <summary>Optional RNG seed; null = nondeterministic.</summary>
    public int? Seed { get; set; }
}

/// <summary>
/// Generates a single-line melody that sits over a chord progression. The melody is diatonic to the
/// key, favours the sounding chord's tones on strong beats, moves mostly stepwise between them, and
/// rests on weak steps according to <see cref="MelodyOptions.Density"/> — so it reads as a melody
/// overlaid on the chords rather than a random scale run.
/// </summary>
public static class MelodyGenerator
{
    public static IReadOnlyList<MidiNote> Generate(
        IReadOnlyList<GeneratedChord> chords, int keyRoot, ScaleType scale, MelodyOptions options)
    {
        var notes = new List<MidiNote>();
        if (chords.Count == 0 || options.StepBeats <= 0) return notes;

        var rng = options.Seed is { } seed ? new Random(seed) : new Random();

        // Pitch classes of the key's scale — the melody only ever picks from these.
        var scaleClasses = new HashSet<int>();
        foreach (var iv in MusicTheory.ScaleIntervals(scale)) scaleClasses.Add(Mod12(keyRoot + iv));

        // A ~2-octave window of scale tones centred on the melody register.
        var center = MusicTheory.ClampNote(keyRoot + 12 * Math.Clamp(options.OctaveOffset, 1, 4));
        var window = new List<int>();
        for (var n = center - 9; n <= center + 16; n++)
            if (n is >= 0 and <= 127 && scaleClasses.Contains(Mod12(n))) window.Add(n);
        if (window.Count == 0) return notes;

        var gate = Math.Clamp(options.Gate, 0.05, 1.0);
        var density = Math.Clamp(options.Density, 0.0, 1.0);
        var prev = NearestInWindow(window, center);

        foreach (var chord in chords)
        {
            var chordClasses = new HashSet<int>();
            foreach (var p in chord.Pitches) chordClasses.Add(Mod12(p));

            var end = chord.StartBeat + chord.LengthBeats;
            var firstStep = true;
            for (var beat = chord.StartBeat; beat < end - 1e-9; beat += options.StepBeats)
            {
                // Strong = the chord's downbeat or any whole beat; these land on chord tones.
                var strong = firstStep || IsWholeBeat(beat);
                firstStep = false;

                // Weak steps may rest (the chord downbeat always sounds, for presence).
                if (!strong && rng.NextDouble() > density) continue;

                var pool = strong
                    ? window.Where(n => chordClasses.Contains(Mod12(n))).ToList()
                    : window;
                if (pool.Count == 0) pool = window;

                prev = PickNear(pool, prev, rng);

                var length = Math.Min(options.StepBeats * gate, end - beat);
                notes.Add(new MidiNote
                {
                    Note = MusicTheory.ClampNote(prev),
                    StartBeat = beat,
                    LengthBeats = length,
                    Velocity = options.Velocity
                });
            }
        }

        return notes;
    }

    private static int Mod12(int n) => ((n % 12) + 12) % 12;

    private static bool IsWholeBeat(double beat) => Math.Abs(beat - Math.Round(beat)) < 1e-6;

    private static int NearestInWindow(IReadOnlyList<int> window, int target)
    {
        var best = window[0];
        foreach (var n in window)
            if (Math.Abs(n - target) < Math.Abs(best - target)) best = n;
        return best;
    }

    // Favours stepwise motion: choose among the few candidates closest to the previous note.
    private static int PickNear(List<int> pool, int prev, Random rng)
    {
        var ordered = pool.OrderBy(n => Math.Abs(n - prev)).ToList();
        var k = Math.Min(3, ordered.Count);
        return ordered[rng.Next(k)];
    }
}
