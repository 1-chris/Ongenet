using System.IO;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV23Tests
{
    [Fact]
    public void MidiFxChain_and_RackSettings_RoundTrip()
    {
        var midi = new MidiEffectRegistry();
        var project = new Project();
        var track = new Track { Name = "FX", Kind = TrackKind.Instrument };
        track.MidiEffects.Add(new ScaleMidiEffect { Root = 2, Minor = true });
        track.MidiEffects.Add(new ArpMidiEffect { RateBeats = 0.25, Gate = 0.5 });
        track.MidiEffects.Add(new HumanizeMidiEffect { VelocityAmount = 0.3f, TimingMs = 12f });
        track.Rack.Kind = RackKind.DrumPadGrid;
        track.Rack.EnsureDefaultDrumPads(4);
        track.Rack.EnsureDefaultMacros();
        track.Rack.DrumPads[0].InstrumentSlotIndex = 0;
        track.Rack.DrumPads[1].InstrumentSlotIndex = 1;
        track.Rack.Macros[0].TargetParameterId = "0:Gain";
        track.Rack.Macros[0].Value = 0.75;
        track.Instruments.Add(new InstrumentSlot(new OscillatorInstrument()));
        track.Instruments.Add(new InstrumentSlot(new TripleOscInstrument()));
        track.CommitInstruments();
        track.CommitMidiEffects();
        project.Tracks.Add(track);

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry(), midi).Project;

        Assert.Equal(23, ProjectFile.FormatVersion);
        var t = loaded.Tracks.Single();
        Assert.Equal(3, t.MidiEffects.Count);
        Assert.IsType<ScaleMidiEffect>(t.MidiEffects[0]);
        Assert.Equal(2, ((ScaleMidiEffect)t.MidiEffects[0]).Root);
        Assert.True(((ScaleMidiEffect)t.MidiEffects[0]).Minor);
        Assert.IsType<ArpMidiEffect>(t.MidiEffects[1]);
        Assert.IsType<HumanizeMidiEffect>(t.MidiEffects[2]);
        Assert.Equal(RackKind.DrumPadGrid, t.Rack.Kind);
        Assert.Equal(4, t.Rack.DrumPads.Count);
        Assert.Equal(1, t.Rack.DrumPads[1].InstrumentSlotIndex);
        Assert.Equal("0:Gain", t.Rack.Macros[0].TargetParameterId);
        Assert.Equal(0.75, t.Rack.Macros[0].Value, 3);
    }

    [Fact]
    public void ClonerPreservesMidiFxAndRack()
    {
        var midi = new MidiEffectRegistry();
        var project = new Project();
        var track = new Track { Name = "Clone", Kind = TrackKind.Instrument };
        track.MidiEffects.Add(new QuantizeMidiEffect { Strength = 0.75f });
        track.Rack.Kind = RackKind.DrumPadGrid;
        track.Rack.EnsureDefaultDrumPads(2);
        track.Instruments.Add(new InstrumentSlot(new OscillatorInstrument()));
        track.CommitInstruments();
        track.CommitMidiEffects();
        project.Tracks.Add(track);

        var clone = ProjectCloner.Clone(project, new InstrumentRegistry(), new EffectRegistry(), midi);
        var t = clone.Tracks.Single();
        Assert.Single(t.MidiEffects);
        Assert.IsType<QuantizeMidiEffect>(t.MidiEffects[0]);
        Assert.Equal(RackKind.DrumPadGrid, t.Rack.Kind);
        Assert.Equal(2, t.Rack.DrumPads.Count);
    }
}
