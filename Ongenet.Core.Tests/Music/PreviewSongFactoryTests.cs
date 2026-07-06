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
/// Verifies the "First Light" preview song: its structure, key, Field/scope/wavetable usage and
/// kick-triggered sidechain — and that it survives an .ongen save/load round-trip, proving the song
/// is fully reproducible through the standard persistence path.
/// </summary>
public class PreviewSongFactoryTests
{
    // C major pitch classes (C D E F G A B).
    private static readonly int[] CMajor = { 0, 2, 4, 5, 7, 9, 11 };

    private static IInstrumentRegistry Registry()
    {
        var instruments = new InstrumentRegistry();
        FieldBootstrap.Initialize(new FieldNodeRegistry(), instruments, new EffectRegistry());
        return instruments;
    }

    [Fact]
    public void SongHasExpectedGlobals()
    {
        var song = PreviewSongFactory.Create(Registry());

        Assert.Equal("First Light", song.Name);
        Assert.Equal(128.0, song.Tempo.BeatsPerMinute);
        Assert.Equal(64, song.BarCount);
        Assert.NotNull(song.Master);
        Assert.Contains(song.Master!.Effects, e => e is WaveformVisualizerEffect); // the 3D scope
        Assert.Contains(song.Master!.Effects, e => e is LimiterEffect);
    }

    [Fact]
    public void LeadIsAFieldPatchWithScopeAndWavetable()
    {
        var song = PreviewSongFactory.Create(Registry());

        var lead = song.Tracks.Single(t => t.Name == "Lead");
        var field = Assert.IsType<FieldInstrument>(lead.PrimaryInstrument);
        Assert.Contains(field.Graph.Nodes, n => n is ScopeNode);        // 3D waveform trail
        Assert.Contains(field.Graph.Nodes, n => n is WavetableOscNode); // 3D wavetable view
    }

    [Fact]
    public void BassSidechainsFromTheKickTrack()
    {
        var song = PreviewSongFactory.Create(Registry());

        var kick = song.Tracks.Single(t => t.Name == "Kick");
        var bass = song.Tracks.Single(t => t.Name == "Bass");
        var sidechain = Assert.IsType<SidechainEffect>(bass.Effects[0]);
        Assert.Equal(kick.Id, sidechain.SourceTrackId);
        Assert.True(sidechain.IsTrackMode);
    }

    [Fact]
    public void AllNotesAreInCMajorAndWithinTheArrangement()
    {
        var song = PreviewSongFactory.Create(Registry());
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
                    Assert.Contains(note.Note % 12, CMajor);
                    Assert.True(note.StartBeat >= 0 && note.StartBeat + note.LengthBeats <= clip.LengthBeats + 1e-6,
                        $"note in '{clip.Name}' must lie within its clip");
                }
            }
        }
    }

    [Fact]
    public void EveryInstrumentTrackIsCommittedForTheAudioThread()
    {
        var song = PreviewSongFactory.Create(Registry());

        foreach (var track in song.Tracks)
        {
            Assert.Equal(track.Instruments.Count, track.ActiveInstruments.Length);
            Assert.Equal(track.Effects.Count, track.ActiveEffects.Length);
            Assert.Equal(track.AutoLanes.Count, track.ActiveAutoLanes.Length);
        }
    }

    [Fact]
    public void SongIsDeterministic()
    {
        var a = PreviewSongFactory.Create(Registry());
        var b = PreviewSongFactory.Create(Registry());

        Assert.Equal(a.Tracks.Select(t => t.Name), b.Tracks.Select(t => t.Name));
        foreach (var (ta, tb) in a.Tracks.Zip(b.Tracks))
        {
            Assert.Equal(ta.Clips.Count, tb.Clips.Count);
            Assert.Equal(
                ta.Clips.SelectMany(c => c.Notes).Select(n => (n.Note, n.StartBeat, n.LengthBeats, n.Velocity)),
                tb.Clips.SelectMany(c => c.Notes).Select(n => (n.Note, n.StartBeat, n.LengthBeats, n.Velocity)));
            Assert.Equal(ta.AutoLanes.Count, tb.AutoLanes.Count);
        }
    }

    [Fact]
    public void SongRoundTripsThroughProjectFile()
    {
        var instruments = new InstrumentRegistry();
        FieldBootstrap.Initialize(new FieldNodeRegistry(), instruments, new EffectRegistry());
        var song = PreviewSongFactory.Create(instruments);

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
            Assert.Equal(before.Clips.Count, after.Clips.Count);
            Assert.Equal(before.Clips.Sum(c => c.Notes.Count), after.Clips.Sum(c => c.Notes.Count));
            Assert.Equal(before.Effects.Count, after.Effects.Count);
            Assert.Equal(before.AutoLanes.Count, after.AutoLanes.Count);
        }

        // The Field lead's graph (scope + wavetable) survives the round-trip.
        var lead = loaded.Tracks.Single(t => t.Name == "Lead");
        var field = Assert.IsType<FieldInstrument>(lead.PrimaryInstrument);
        Assert.Contains(field.Graph.Nodes, n => n is ScopeNode);
        Assert.Contains(field.Graph.Nodes, n => n is WavetableOscNode);

        // The sidechain still points at the (re-loaded) kick track.
        var kick = loaded.Tracks.Single(t => t.Name == "Kick");
        var bass = loaded.Tracks.Single(t => t.Name == "Bass");
        var sidechain = Assert.IsType<SidechainEffect>(bass.Effects[0]);
        Assert.Equal(kick.Id, sidechain.SourceTrackId);
    }
}
