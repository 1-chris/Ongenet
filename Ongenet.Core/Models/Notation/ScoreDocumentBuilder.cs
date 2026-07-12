using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Music;

namespace Ongenet.Core.Models.Notation;

/// <summary>Builds a <see cref="ScoreDocument"/> from project MIDI clips (piano-roll → staff).</summary>
public static class ScoreDocumentBuilder
{
    public static ScoreDocument FromProject(Project project, int beatsPerBar = 4)
    {
        var bar = Math.Max(1, beatsPerBar);
        var doc = new ScoreDocument
        {
            Title = project.Name,
            Divisions = 480
        };

        foreach (var track in project.Tracks.Where(t => t.Kind == TrackKind.Instrument))
        {
            var staff = new ScoreStaff
            {
                TrackId = track.Id,
                Clef = InferClef(track)
            };

            foreach (var clip in track.Clips.Where(c => !c.IsAudio))
            {
                foreach (var note in clip.Notes)
                {
                    staff.Notes.Add(new ScoreNote
                    {
                        Pitch = note.Note,
                        StartBeat = clip.StartBeat + note.StartBeat,
                        LengthBeats = note.LengthBeats,
                        Velocity = (int)(note.Velocity * 127)
                    });
                }
            }

            if (staff.Notes.Count > 0)
            {
                InferChordSymbols(staff, bar);
                doc.Staves.Add(staff);
            }
        }

        if (doc.Staves.Count > 0)
        {
            var part = new ScorePart { Name = project.Name };
            foreach (var staff in doc.Staves)
                part.Staves.Add(staff);
            doc.Parts.Add(part);
        }

        return doc;
    }

    private static string InferClef(Track track)
    {
        var pitches = track.Clips.Where(c => !c.IsAudio).SelectMany(c => c.Notes).Select(n => n.Note).ToList();
        if (pitches.Count == 0) return "treble";
        var avg = pitches.Average();
        return avg < 55 ? "bass" : "treble";
    }

    /// <summary>Places chord symbols at each measure start from simultaneous note clusters.</summary>
    private static void InferChordSymbols(ScoreStaff staff, int beatsPerBar)
    {
        if (staff.Notes.Count == 0) return;

        var endBeat = staff.Notes.Max(n => n.StartBeat + n.LengthBeats);
        var measureCount = Math.Max(1, (int)Math.Ceiling(endBeat / beatsPerBar));

        for (var m = 0; m < measureCount; m++)
        {
            var measureStart = m * beatsPerBar;
            var measureEnd = measureStart + beatsPerBar;
            var atStart = staff.Notes
                .Where(n => n.StartBeat >= measureStart && n.StartBeat < measureStart + 0.25)
                .Select(n => n.Pitch)
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            if (atStart.Count < 2) continue;

            var symbol = GuessChordSymbol(atStart);
            if (symbol.Length == 0) continue;

            staff.ChordSymbols.Add(new ScoreChordSymbol
            {
                StartBeat = measureStart,
                MeasureNumber = m + 1,
                Text = symbol
            });
        }
    }

    private static string GuessChordSymbol(IReadOnlyList<int> pitches)
    {
        var classes = pitches.Select(p => ((p % 12) + 12) % 12).Distinct().OrderBy(c => c).ToList();
        if (classes.Count < 2) return string.Empty;

        var root = classes[0];
        var intervals = classes.Select(c => (c - root + 12) % 12).OrderBy(i => i).ToList();

        var rootName = MusicTheory.PitchClassName(root);
        if (intervals.Contains(3) && intervals.Contains(7))
            return rootName + "m";
        if (intervals.Contains(4) && intervals.Contains(7))
            return rootName;
        if (intervals.Contains(3) && intervals.Contains(6))
            return rootName + "dim";
        if (intervals.Contains(4) && intervals.Contains(7) && intervals.Contains(11))
            return rootName + "maj7";
        if (intervals.Contains(3) && intervals.Contains(7) && intervals.Contains(10))
            return rootName + "m7";
        if (intervals.Contains(4) && intervals.Contains(7) && intervals.Contains(10))
            return rootName + "7";

        return rootName;
    }
}
