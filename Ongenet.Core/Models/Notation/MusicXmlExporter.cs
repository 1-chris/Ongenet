using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Ongenet.Core.Models.Notation;

/// <summary>Exports a <see cref="ScoreDocument"/> to MusicXML 3.1 (.musicxml).</summary>
public static class MusicXmlExporter
{
    private static readonly XNamespace Mxl = "http://www.musicxml.org/ns/score-partwise";

    public static void Export(ScoreDocument doc, string path, int beatsPerBar = 4)
    {
        var root = new XElement(Mxl + "score-partwise",
            new XAttribute("version", "3.1"),
            new XElement(Mxl + "work",
                new XElement(Mxl + "work-title", doc.Title)),
            BuildPartList(doc),
            doc.Staves.Select((staff, i) => BuildPart(staff, i + 1, doc.Divisions, beatsPerBar)));

        var declaration = new XDeclaration("1.0", "UTF-8", null);
        var document = new XDocument(declaration, root);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        document.Save(writer, SaveOptions.None);
    }

    private static XElement BuildPartList(ScoreDocument doc)
    {
        var list = new XElement(Mxl + "part-list");
        for (var i = 0; i < doc.Staves.Count; i++)
        {
            list.Add(new XElement(Mxl + "score-part",
                new XAttribute("id", PartId(i + 1)),
                new XElement(Mxl + "part-name", $"Staff {i + 1}")));
        }
        return list;
    }

    private static XElement BuildPart(ScoreStaff staff, int partNumber, int divisions, int beatsPerBar)
    {
        var part = new XElement(Mxl + "part", new XAttribute("id", PartId(partNumber)));
        var notes = staff.Notes.OrderBy(n => n.StartBeat).ToList();
        if (notes.Count == 0)
        {
            part.Add(BuildMeasure(1, divisions, beatsPerBar, staff.Clef, Array.Empty<ScoreNote>()));
            return part;
        }

        var endBeat = notes.Max(n => n.StartBeat + n.LengthBeats);
        var measureCount = Math.Max(1, (int)Math.Ceiling(endBeat / beatsPerBar));
        for (var m = 0; m < measureCount; m++)
        {
            var measureStart = m * beatsPerBar;
            var measureEnd = measureStart + beatsPerBar;
            var measureNotes = notes
                .Where(n => n.StartBeat >= measureStart && n.StartBeat < measureEnd)
                .ToList();
            part.Add(BuildMeasure(m + 1, divisions, beatsPerBar, staff.Clef, measureNotes, measureStart));
        }

        return part;
    }

    private static XElement BuildMeasure(int number, int divisions, int beatsPerBar, string clef,
        IReadOnlyList<ScoreNote> notes, double measureStart = 0)
    {
        var measure = new XElement(Mxl + "measure", new XAttribute("number", number));
        if (number == 1)
        {
            measure.Add(new XElement(Mxl + "attributes",
                new XElement(Mxl + "divisions", divisions),
                new XElement(Mxl + "time",
                    new XElement(Mxl + "beats", beatsPerBar),
                    new XElement(Mxl + "beat-type", 4)),
                new XElement(Mxl + "clef",
                    new XElement(Mxl + "sign", clef == "bass" ? "F" : "G"),
                    new XElement(Mxl + "line", clef == "bass" ? 4 : 2))));
        }

        var cursor = 0.0;
        foreach (var note in notes.OrderBy(n => n.StartBeat))
        {
            var localStart = note.StartBeat - measureStart;
            var gap = localStart - cursor;
            if (gap > 0.001)
                measure.Add(BuildRest(gap, divisions));

            measure.Add(BuildNote(note, divisions));
            cursor = localStart + note.LengthBeats;
        }

        var remaining = beatsPerBar - cursor;
        if (remaining > 0.001)
            measure.Add(BuildRest(remaining, divisions));

        return measure;
    }

    private static XElement BuildNote(ScoreNote note, int divisions)
    {
        var (step, octave, alter) = MidiToPitch(note.Pitch);
        var duration = Math.Max(1, (int)Math.Round(note.LengthBeats * divisions));
        var pitch = new XElement(Mxl + "pitch",
            new XElement(Mxl + "step", step),
            new XElement(Mxl + "octave", octave));
        if (alter != 0)
            pitch.Add(new XElement(Mxl + "alter", alter.ToString(CultureInfo.InvariantCulture)));

        return new XElement(Mxl + "note",
            pitch,
            new XElement(Mxl + "duration", duration),
            new XElement(Mxl + "type", DurationType(note.LengthBeats)),
            new XElement(Mxl + "velocity", note.Velocity));
    }

    private static XElement BuildRest(double lengthBeats, int divisions)
    {
        var duration = Math.Max(1, (int)Math.Round(lengthBeats * divisions));
        return new XElement(Mxl + "note",
            new XElement(Mxl + "rest"),
            new XElement(Mxl + "duration", duration),
            new XElement(Mxl + "type", DurationType(lengthBeats)));
    }

    private static (string Step, int Octave, int Alter) MidiToPitch(int midi)
    {
        var names = new[] { ("C", 0), ("C", 1), ("D", 0), ("D", 1), ("E", 0), ("F", 0), ("F", 1), ("G", 0), ("G", 1), ("A", 0), ("A", 1), ("B", 0) };
        var idx = Math.Clamp(midi, 0, 127);
        var (step, alter) = names[idx % 12];
        var octave = idx / 12 - 1;
        return (step, octave, alter);
    }

    private static string DurationType(double beats)
    {
        if (beats >= 3.5) return "whole";
        if (beats >= 1.75) return "half";
        if (beats >= 0.875) return "quarter";
        if (beats >= 0.4375) return "eighth";
        return "16th";
    }

    private static string PartId(int n) => $"P{n}";
}
