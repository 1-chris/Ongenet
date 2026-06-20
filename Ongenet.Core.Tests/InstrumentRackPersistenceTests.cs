using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests;

/// <summary>Round-trips an instrument track with a multi-instrument rack through save/load and the cloner.</summary>
public class InstrumentRackPersistenceTests
{
    private static Project BuildProject()
    {
        var project = new Project();
        var track = new Track { Name = "Rack", Kind = TrackKind.Instrument };

        var s1 = new InstrumentSlot(new OscillatorInstrument()) { Enabled = true };
        s1.Effects.Add(new DelayEffect());
        s1.CommitEffects();

        var s2 = new InstrumentSlot(new TripleOscInstrument()) { Enabled = false };
        s2.CommitEffects();

        track.Instruments.Add(s1);
        track.Instruments.Add(s2);
        track.CommitInstruments();
        project.Tracks.Add(track);
        return project;
    }

    [Fact]
    public void SaveLoadPreservesInstrumentRack()
    {
        var project = BuildProject();

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 0, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        var track = loaded.Tracks.Single();
        Assert.Equal(2, track.Instruments.Count);
        Assert.IsType<OscillatorInstrument>(track.Instruments[0].Instrument);
        Assert.True(track.Instruments[0].Enabled);
        Assert.Single(track.Instruments[0].Effects);
        Assert.IsType<DelayEffect>(track.Instruments[0].Effects[0]);

        Assert.IsType<TripleOscInstrument>(track.Instruments[1].Instrument);
        Assert.False(track.Instruments[1].Enabled);

        // The audio-thread snapshot is populated on load.
        Assert.Equal(2, track.ActiveInstruments.Length);
        Assert.Single(track.ActiveInstruments[0].ActiveEffects);
    }

    [Fact]
    public void ClonerPreservesInstrumentRack()
    {
        var project = BuildProject();
        var clone = ProjectCloner.Clone(project, new InstrumentRegistry(), new EffectRegistry());

        var track = clone.Tracks.Single();
        Assert.Equal(2, track.Instruments.Count);
        Assert.False(track.Instruments[1].Enabled);
        Assert.Single(track.Instruments[0].Effects);
        Assert.Equal(2, track.ActiveInstruments.Length);
    }
}
