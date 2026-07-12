using System;
using System.Collections.Generic;
using System.IO;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Notation;
using Xunit;

namespace Ongenet.Core.Tests.Notation;

public sealed class MusicXmlImporterTests
{
    [Fact]
    public void RoundTrip_PreservesNotePitchAndTiming()
    {
        var doc = new ScoreDocument { Title = "Test", Divisions = 480 };
        var staff = new ScoreStaff();
        staff.Notes.Add(new ScoreNote { Pitch = 60, StartBeat = 0, LengthBeats = 1, Velocity = 100 });
        staff.Notes.Add(new ScoreNote { Pitch = 64, StartBeat = 1, LengthBeats = 1, Velocity = 90 });
        doc.Staves.Add(staff);

        var path = Path.Combine(Path.GetTempPath(), $"ongenet-mxl-{Guid.NewGuid():N}.musicxml");
        try
        {
            MusicXmlExporter.Export(doc, path, beatsPerBar: 4);
            var imported = MusicXmlImporter.Import(path);
            Assert.Equal("Test", imported.Title);
            Assert.Single(imported.Staves);
            Assert.Equal(2, imported.Staves[0].Notes.Count);
            Assert.Contains(imported.Staves[0].Notes, n => n.Pitch == 60 && n.StartBeat == 0);
            Assert.Contains(imported.Staves[0].Notes, n => n.Pitch == 64 && n.StartBeat == 1);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ImportToProject_AddsInstrumentTrackWithMidiClip()
    {
        var doc = new ScoreDocument { Title = "Imported" };
        var staff = new ScoreStaff();
        staff.Notes.Add(new ScoreNote { Pitch = 72, StartBeat = 0, LengthBeats = 2, Velocity = 100 });
        doc.Staves.Add(staff);

        var path = Path.Combine(Path.GetTempPath(), $"ongenet-mxl-{Guid.NewGuid():N}.musicxml");
        try
        {
            MusicXmlExporter.Export(doc, path);
            var project = new Project();
            project.Tracks.Add(new Track { Kind = TrackKind.Master });
            MusicXmlImporter.ImportToProject(project, path);

            var track = Assert.Single(project.Tracks, t => t.Kind == TrackKind.Instrument);
            var clip = Assert.Single(track.Clips);
            Assert.False(clip.IsAudio);
            Assert.Single(clip.Notes, n => n.Note == 72);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
