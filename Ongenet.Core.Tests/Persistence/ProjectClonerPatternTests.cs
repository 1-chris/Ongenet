using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectClonerPatternTests
{
    [Fact]
    public void Clone_PreservesPatternsAndClips()
    {
        var instruments = new InstrumentRegistry();
        var effects = new EffectRegistry();
        var project = new Project { Name = "Clone" };
        var patternTrack = PatternTrackHelper.CreatePatternTrack(project);
        project.Tracks.Add(patternTrack);
        var pattern = project.Patterns[0];
        var inst = new Track { Name = "Kick", Kind = TrackKind.Instrument };
        inst.Instruments.Add(new InstrumentSlot(new BasicSamplerInstrument()));
        inst.CommitInstruments();
        project.Tracks.Insert(0, inst);
        PatternTrackHelper.AddInstrumentRow(pattern, inst);
        project.PatternClips.Add(new PatternClip { PatternId = pattern.Id, TrackId = patternTrack.Id, LengthBeats = 4 });

        var clone = ProjectCloner.Clone(project, instruments, effects);

        Assert.Single(clone.Patterns);
        Assert.Single(clone.PatternClips);
        Assert.Equal(pattern.Id, clone.Patterns[0].Id);
        Assert.Single(clone.Patterns[0].Channels);
        Assert.Equal(patternTrack.ActivePatternId, clone.Tracks.First(t => t.Kind == TrackKind.Pattern).ActivePatternId);
    }
}
