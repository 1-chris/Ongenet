using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV5Tests
{
    [Fact]
    public void PatternTrackAndRowMetadata_RoundTrip()
    {
        var project = new Project { Name = "V5 Test" };
        var patternTrack = PatternTrackHelper.CreatePatternTrack(project);
        project.Tracks.Add(patternTrack);
        var pattern = project.Patterns[0];
        pattern.Name = "Drums";

        var inst = new Track { Name = "Kick", Kind = TrackKind.Instrument };
        inst.Instruments.Add(new InstrumentSlot(new BasicSamplerInstrument()));
        inst.CommitInstruments();
        project.Tracks.Insert(0, inst);

        var sampleClip = new Clip { Name = "Snare.wav", IsAudio = true, LengthBeats = 4 };
        inst.Clips.Add(sampleClip);

        var instRow = PatternTrackHelper.AddInstrumentRow(pattern, inst);
        instRow.Order = 0;

        var samplerTrack = new Track { Name = "Snare", Kind = TrackKind.Instrument };
        samplerTrack.Instruments.Add(new InstrumentSlot(new BasicSamplerInstrument()));
        samplerTrack.CommitInstruments();
        project.Tracks.Insert(1, samplerTrack);
        var sampleRow = PatternTrackHelper.AddSampleRow(pattern, samplerTrack, sampleClip, 38);
        sampleRow.Order = 1;
        pattern.GetOrCreateSequence(instRow).Steps[0].Active = true;

        project.PatternClips.Add(new PatternClip
        {
            PatternId = pattern.Id,
            TrackId = patternTrack.Id,
            StartBeat = 0,
            LengthBeats = 4
        });

        using var ms = new System.IO.MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 16, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        var loadedPatternTrack = loaded.Tracks.First(t => t.Kind == TrackKind.Pattern);
        Assert.Equal(pattern.Id, loadedPatternTrack.ActivePatternId);
        var loadedPattern = loaded.Patterns.First(p => p.Id == pattern.Id);
        Assert.Equal(2, loadedPattern.Channels.Count);
        Assert.Equal(PatternRowSourceKind.AudioSample, loadedPattern.Channels.First(c => c.Id == sampleRow.Id).SourceKind);
        Assert.Equal(sampleClip.Id, loadedPattern.Channels.First(c => c.Id == sampleRow.Id).SampleClipId);
        Assert.True(loadedPattern.StepSequences[0].Steps[0].Active);
        Assert.Single(loaded.PatternClips);
    }
}
