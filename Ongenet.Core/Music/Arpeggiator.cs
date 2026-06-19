using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Music;

/// <summary>Pattern direction for the arpeggiator.</summary>
public enum ArpMode
{
    Up,
    Down,
    UpDown,
    DownUp,
    Random,
    AsPlayed
}

/// <summary>Parameters for <see cref="Arpeggiator.Arpeggiate"/>.</summary>
public sealed class ArpOptions
{
    /// <summary>Pattern direction.</summary>
    public ArpMode Mode { get; set; } = ArpMode.Up;

    /// <summary>Number of octaves the pattern spans (1..4).</summary>
    public int Octaves { get; set; } = 1;

    /// <summary>Length of one arp step, in beats (e.g. 0.25 = a 1/16 in 4/4).</summary>
    public double StepBeats { get; set; } = 0.25;

    /// <summary>Note length as a fraction of the step, 0..1 (0.5 = staccato-ish).</summary>
    public double Gate { get; set; } = 0.9;

    /// <summary>Note velocity, 0..1.</summary>
    public float Velocity { get; set; } = 0.8f;

    /// <summary>Optional RNG seed (only used by <see cref="ArpMode.Random"/>).</summary>
    public int? Seed { get; set; }
}

/// <summary>
/// Turns a set of (overlapping) pitches into an arpeggiated sequence of <see cref="MidiNote"/>s
/// laid across a beat span — the engine behind "Convert to arpeggio".
/// </summary>
public static class Arpeggiator
{
    public static IReadOnlyList<MidiNote> Arpeggiate(
        IReadOnlyList<int> pitches, double spanStartBeat, double spanLengthBeats, ArpOptions options)
    {
        var notes = new List<MidiNote>();
        if (pitches.Count == 0 || spanLengthBeats <= 0 || options.StepBeats <= 0) return notes;

        var sequence = BuildSequence(pitches, options);
        if (sequence.Count == 0) return notes;

        var rng = options.Seed is { } seed ? new Random(seed) : new Random();
        var gate = Math.Clamp(options.Gate, 0.05, 1.0);
        var spanEnd = spanStartBeat + spanLengthBeats;

        var step = 0;
        for (var beat = spanStartBeat; beat < spanEnd - 1e-9; beat += options.StepBeats, step++)
        {
            var pitch = options.Mode == ArpMode.Random
                ? sequence[rng.Next(sequence.Count)]
                : sequence[step % sequence.Count];

            var length = Math.Min(options.StepBeats * gate, spanEnd - beat);
            notes.Add(new MidiNote
            {
                Note = MusicTheory.ClampNote(pitch),
                StartBeat = beat,
                LengthBeats = length,
                Velocity = options.Velocity
            });
        }

        return notes;
    }

    /// <summary>Builds the ordered pitch sequence (one cycle) for the chosen mode and octave range.</summary>
    private static List<int> BuildSequence(IReadOnlyList<int> pitches, ArpOptions options)
    {
        var octaves = Math.Clamp(options.Octaves, 1, 4);

        // AsPlayed keeps the caller's order; everything else works from a sorted, de-duplicated set.
        var basePitches = options.Mode == ArpMode.AsPlayed
            ? pitches.ToList()
            : pitches.Distinct().OrderBy(p => p).ToList();

        var expanded = new List<int>();
        for (var oct = 0; oct < octaves; oct++)
            foreach (var p in basePitches)
                expanded.Add(p + 12 * oct);

        switch (options.Mode)
        {
            case ArpMode.Down:
                expanded.Reverse();
                break;
            case ArpMode.UpDown when expanded.Count > 1:
            {
                var down = new List<int>(expanded);
                down.Reverse();
                // Drop the shared endpoints so the turn-arounds don't repeat a note.
                expanded.AddRange(down.Skip(1).Take(down.Count - 2));
                break;
            }
            case ArpMode.DownUp when expanded.Count > 1:
            {
                var asc = new List<int>(expanded);
                expanded.Reverse();
                expanded.AddRange(asc.Skip(1).Take(asc.Count - 2));
                break;
            }
        }

        return expanded;
    }
}
