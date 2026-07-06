using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Music;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Tests.Music;

/// <summary>
/// Verifies the "Undertow" dark DnB built-in project: key/tempo/structure, the Field Reese bass,
/// kick-triggered sidechains, and .ongen round-trip fidelity.
/// </summary>
public class DarkDnbSongFactoryTests
{
    // F natural minor pitch classes (F G Ab Bb C Db Eb).
    private static readonly int[] FMinor = { 5, 7, 8, 10, 0, 1, 3 };

    private static IInstrumentRegistry Registry()
    {
        var instruments = new InstrumentRegistry();
        FieldBootstrap.Initialize(new FieldNodeRegistry(), instruments, new EffectRegistry());
        return instruments;
    }

    [Fact]
    public void SongHasExpectedGlobals()
    {
        var song = DarkDnbSongFactory.Create(Registry());

        Assert.Equal("Undertow", song.Name);
        Assert.Equal(170.0, song.Tempo.BeatsPerMinute);
        Assert.Equal(96, song.BarCount);
        Assert.NotNull(song.Master);
        Assert.Contains(song.Master!.Effects, e => e is WaveformVisualizerEffect);
    }

    [Fact]
    public void ReeseAndSubSidechainFromTheKick()
    {
        var song = DarkDnbSongFactory.Create(Registry());
        var kick = song.Tracks.Single(t => t.Name == "Kick");

        foreach (var name in new[] { "Reese", "Sub" })
        {
            var track = song.Tracks.Single(t => t.Name == name);
            var sidechain = Assert.IsType<SidechainEffect>(track.Effects[0]);
            Assert.Equal(kick.Id, sidechain.SourceTrackId);
        }
    }

    [Fact]
    public void ReeseIsAFieldPatch()
    {
        var song = DarkDnbSongFactory.Create(Registry());
        var reese = song.Tracks.Single(t => t.Name == "Reese");
        var field = Assert.IsType<FieldInstrument>(reese.PrimaryInstrument);

        // The Reese decomposition: a detuned unison driven into a slowly-breathing filter.
        Assert.Contains(field.Graph.Nodes, n => n is UnisonOscNode);
        Assert.Contains(field.Graph.Nodes, n => n is WaveShaperNode);
        Assert.Contains(field.Graph.Nodes, n => n is BiquadFilterNode);
        Assert.Contains(field.Graph.Nodes, n => n is LfoNode);
    }

    [Fact]
    public void LeadIsTheNovaSawSupersaw()
    {
        var song = DarkDnbSongFactory.Create(Registry());
        var lead = song.Tracks.Single(t => t.Name == "Lead");
        var field = Assert.IsType<FieldInstrument>(lead.PrimaryInstrument);

        var uni = field.Graph.Nodes.OfType<UnisonOscNode>().Single();
        Assert.Equal(9, uni.Voices);
        Assert.True(uni.StereoWidth > 0.5, "the supersaw should be wide");
    }

    [Fact]
    public void PadsHaveAQuarterNoteTempoPump()
    {
        var song = DarkDnbSongFactory.Create(Registry());
        var pads = song.Tracks.Single(t => t.Name == "Pads");
        var pump = pads.Effects.OfType<SidechainEffect>().Single();

        Assert.False(pump.IsTrackMode); // tempo-synced, not track-triggered
        Assert.Equal(2, pump.RateIndex); // 1/4-note division
    }

    [Fact]
    public void AllNotesAreInFMinorAndWithinTheArrangement()
    {
        var song = DarkDnbSongFactory.Create(Registry());
        var endBeat = song.BarCount * 4.0;

        foreach (var track in song.Tracks.Where(t => t.Kind == TrackKind.Instrument))
        {
            Assert.NotEmpty(track.Clips);
            foreach (var clip in track.Clips)
            {
                Assert.True(clip.StartBeat >= 0 && clip.EndBeat <= endBeat,
                    $"clip '{clip.Name}' on '{track.Name}' must lie within the arrangement");
                Assert.NotEmpty(clip.Notes);
                foreach (var note in clip.Notes)
                {
                    Assert.Contains(note.Note % 12, FMinor);
                    Assert.True(note.StartBeat >= 0 && note.StartBeat + note.LengthBeats <= clip.LengthBeats + 1e-6,
                        $"note in '{clip.Name}' must lie within its clip");
                }
            }
        }
    }

    [Fact]
    public void SnareRollsClimbIntoEachDrop()
    {
        var song = DarkDnbSongFactory.Create(Registry());
        var snare = song.Tracks.Single(t => t.Name == "Snare");
        var rolls = snare.Clips.Where(c => c.Name == "Snare Roll").ToList();

        Assert.Equal(2, rolls.Count);
        foreach (var roll in rolls)
        {
            Assert.Equal(32, roll.Notes.Count); // two bars of 16ths
            Assert.True(roll.Notes[^1].Velocity > roll.Notes[0].Velocity, "the roll should crescendo");
        }
    }

    [Fact]
    public void SongRoundTripsThroughProjectFile()
    {
        var instruments = new InstrumentRegistry();
        FieldBootstrap.Initialize(new FieldNodeRegistry(), instruments, new EffectRegistry());
        var song = DarkDnbSongFactory.Create(instruments);

        using var ms = new MemoryStream();
        ProjectFile.Save(song, ms, "test", 0, 0, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, instruments, new EffectRegistry()).Project;

        Assert.Equal(song.Name, loaded.Name);
        Assert.Equal(song.Tempo.BeatsPerMinute, loaded.Tempo.BeatsPerMinute);
        Assert.Equal(song.Tracks.Count, loaded.Tracks.Count);
        foreach (var (before, after) in song.Tracks.Zip(loaded.Tracks))
        {
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Clips.Sum(c => c.Notes.Count), after.Clips.Sum(c => c.Notes.Count));
            Assert.Equal(before.AutoLanes.Count, after.AutoLanes.Count);
        }

        var reese = loaded.Tracks.Single(t => t.Name == "Reese");
        Assert.IsType<FieldInstrument>(reese.PrimaryInstrument);
    }

    [Fact]
    public void EveryCatalogEntryBuildsAFreshMatchingProject()
    {
        var instruments = Registry();
        foreach (var info in BuiltInProjects.All)
        {
            var project = info.Create(instruments);
            Assert.Equal(info.Name, project.Name);
            Assert.NotSame(project, info.Create(instruments));
        }
    }
}
