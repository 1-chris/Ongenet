using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Models.Notation;

/// <summary>Imports MusicXML 3.x (partwise) into a <see cref="ScoreDocument"/> or project MIDI clips.</summary>
public static class MusicXmlImporter
{
    private static readonly XNamespace Mxl = "http://www.musicxml.org/ns/score-partwise";

    public static ScoreDocument Import(string path)
    {
        using var stream = File.OpenRead(path);
        return Import(stream);
    }

    public static ScoreDocument Import(Stream stream)
    {
        var root = XDocument.Load(stream).Root
                   ?? throw new InvalidDataException("Empty MusicXML document.");
        if (root.Name != Mxl + "score-partwise")
            throw new InvalidDataException("Expected score-partwise MusicXML.");

        var title = root.Element(Mxl + "work")?.Element(Mxl + "work-title")?.Value
                    ?? root.Element(Mxl + "movement-title")?.Value
                    ?? "Imported Score";

        var doc = new ScoreDocument { Title = title };
        var partList = root.Element(Mxl + "part-list");
        var partNames = new Dictionary<string, string>();
        if (partList is not null)
        {
            foreach (var sp in partList.Elements(Mxl + "score-part"))
            {
                var id = sp.Attribute("id")?.Value;
                if (id is null) continue;
                partNames[id] = sp.Element(Mxl + "part-name")?.Value ?? id;
            }
        }

        foreach (var part in root.Elements(Mxl + "part"))
        {
            var partId = part.Attribute("id")?.Value ?? "P1";
            var staff = new ScoreStaff
            {
                Clef = "treble"
            };

            var measureOffset = 0.0;
            var divisions = 480;
            var beatsPerBar = 4;

            foreach (var measure in part.Elements(Mxl + "measure"))
            {
                var attrs = measure.Element(Mxl + "attributes");
                if (attrs is not null)
                {
                    if (attrs.Element(Mxl + "divisions")?.Value is { } divStr
                        && int.TryParse(divStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var div))
                        divisions = Math.Max(1, div);

                    var time = attrs.Element(Mxl + "time");
                    if (time?.Element(Mxl + "beats")?.Value is { } beatsStr
                        && int.TryParse(beatsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var beats))
                        beatsPerBar = Math.Max(1, beats);

                    var clefSign = attrs.Element(Mxl + "clef")?.Element(Mxl + "sign")?.Value;
                    if (clefSign == "F") staff.Clef = "bass";
                    else if (clefSign == "G") staff.Clef = "treble";
                }

                var cursor = 0.0;
                foreach (var noteEl in measure.Elements(Mxl + "note"))
                {
                    if (noteEl.Element(Mxl + "chord") is null)
                    {
                        var dur = ReadDuration(noteEl, divisions);
                        cursor += dur;
                    }

                    if (noteEl.Element(Mxl + "rest") is not null) continue;

                    var pitchEl = noteEl.Element(Mxl + "pitch");
                    if (pitchEl is null) continue;

                    var step = pitchEl.Element(Mxl + "step")?.Value ?? "C";
                    var octave = int.Parse(pitchEl.Element(Mxl + "octave")?.Value ?? "4",
                        CultureInfo.InvariantCulture);
                    var alter = 0;
                    if (pitchEl.Element(Mxl + "alter")?.Value is { } alterStr
                        && double.TryParse(alterStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                        alter = (int)Math.Round(a);

                    var lengthBeats = ReadDuration(noteEl, divisions);
                    var startBeat = measureOffset + cursor - lengthBeats;
                    var velocity = 100;
                    if (noteEl.Element(Mxl + "velocity")?.Value is { } velStr
                        && int.TryParse(velStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vel))
                        velocity = Math.Clamp(vel, 1, 127);

                    staff.Notes.Add(new ScoreNote
                    {
                        Pitch = PitchToMidi(step, octave, alter),
                        StartBeat = startBeat,
                        LengthBeats = lengthBeats,
                        Velocity = velocity
                    });
                }

                measureOffset += beatsPerBar;
            }

            if (staff.Notes.Count > 0)
            {
                doc.Divisions = divisions;
                doc.Staves.Add(staff);
            }
        }

        return doc;
    }

    /// <summary>Creates one instrument track per imported staff with a single MIDI clip.</summary>
    public static void ImportToProject(Project project, string path)
    {
        var doc = Import(path);
        ApplyToProject(project, doc, Path.GetFileNameWithoutExtension(path));
    }

    public static void ApplyToProject(Project project, ScoreDocument doc, string? clipBaseName = null)
    {
        clipBaseName ??= string.IsNullOrWhiteSpace(doc.Title) ? "Imported" : doc.Title;
        foreach (var staff in doc.Staves)
        {
            if (staff.Notes.Count == 0) continue;

            var endBeat = staff.Notes.Max(n => n.StartBeat + n.LengthBeats);
            var track = new Track
            {
                Name = $"Score {project.Tracks.Count(t => t.Kind == TrackKind.Instrument)}",
                Kind = TrackKind.Instrument,
                ColorKey = "CatppuccinBlue"
            };
            var clip = new Clip
            {
                Name = clipBaseName,
                IsAudio = false,
                StartBeat = 0,
                LengthBeats = Math.Max(4, Math.Ceiling(endBeat))
            };

            foreach (var note in staff.Notes.OrderBy(n => n.StartBeat))
            {
                clip.Notes.Add(new MidiNote
                {
                    Note = note.Pitch,
                    StartBeat = note.StartBeat,
                    LengthBeats = note.LengthBeats,
                    Velocity = note.Velocity / 127f
                });
            }

            track.Clips.Add(clip);
            project.Tracks.Add(track);
        }
    }

    private static double ReadDuration(XElement noteEl, int divisions)
        => Math.Max(0.25, ReadInt(noteEl, Mxl + "duration") / (double)Math.Max(1, divisions));

    private static int ReadInt(XElement parent, XName name)
        => int.TryParse(parent.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : 0;

    private static int PitchToMidi(string step, int octave, int alter)
    {
        var baseNote = step.ToUpperInvariant() switch
        {
            "C" => 0,
            "D" => 2,
            "E" => 4,
            "F" => 5,
            "G" => 7,
            "A" => 9,
            "B" => 11,
            _ => 0
        };
        return Math.Clamp((octave + 1) * 12 + baseNote + alter, 0, 127);
    }
}
