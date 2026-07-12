using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class PatternSchedulerTests
{
    [Fact]
    public void InstrumentRow_EmitsScheduledNotes()
    {
        var project = BuildProject(out var pattern, out var patternTrack, out var instrumentTrack);
        var channel = PatternTrackHelper.AddInstrumentRow(pattern, instrumentTrack);
        var seq = pattern.GetOrCreateSequence(channel);
        seq.Steps[0].Active = true;
        seq.Steps[0].Note = 36;
        seq.Steps[0].Velocity = 0.9f;

        project.PatternClips.Add(new PatternClip
        {
            PatternId = pattern.Id,
            TrackId = patternTrack.Id,
            StartBeat = 0,
            LengthBeats = 4
        });

        var scheduler = new PatternScheduler();
        var schedule = scheduler.Build(new PlaybackScheduleContext
        {
            Project = project,
            Tracks = project.Tracks,
            StartBeat = 0,
            Channels = 2,
            SampleRate = 48000,
            Bpm = 120
        });

        Assert.Single(schedule.Notes);
        Assert.Equal(instrumentTrack.Id, schedule.Notes[0].TrackId);
        Assert.Equal(36, schedule.Notes[0].Note);
    }

    [Fact]
    public void MicroTiming_ShiftsNoteOnBeat()
    {
        var project = BuildProject(out var pattern, out var patternTrack, out var instrumentTrack);
        var channel = PatternTrackHelper.AddInstrumentRow(pattern, instrumentTrack);
        var seq = pattern.GetOrCreateSequence(channel);
        seq.Steps[0].Active = true;
        seq.Steps[0].Note = 60;
        seq.Steps[0].MicroTimingTicks = 120; // late by 120/480 of one step

        project.PatternClips.Add(new PatternClip
        {
            PatternId = pattern.Id,
            TrackId = patternTrack.Id,
            StartBeat = 0,
            LengthBeats = 4
        });

        var schedule = new PatternScheduler().Build(Context(project));

        Assert.Single(schedule.Notes);
        var stepBeats = pattern.LengthBeats / seq.StepCount;
        var expected = 120 / 480.0 * stepBeats;
        Assert.InRange(schedule.Notes[0].OnBeat, expected - 1e-6, expected + 1e-6);
    }

    [Fact]
    public void MutedRow_IsIgnored()
    {
        var project = BuildProject(out var pattern, out var patternTrack, out var instrumentTrack);
        var channel = PatternTrackHelper.AddInstrumentRow(pattern, instrumentTrack);
        channel.Muted = true;
        pattern.GetOrCreateSequence(channel).Steps[0].Active = true;
        project.PatternClips.Add(new PatternClip { PatternId = pattern.Id, TrackId = patternTrack.Id, LengthBeats = 4 });

        var schedule = new PatternScheduler().Build(Context(project));
        Assert.Empty(schedule.Notes);
    }

    [Fact]
    public void RowReorder_KeepsStepOwnership()
    {
        var project = BuildProject(out var pattern, out _, out var instrumentTrack);
        var kick = PatternTrackHelper.AddInstrumentRow(pattern, instrumentTrack);
        kick.Name = "Kick";
        kick.Order = 0;
        var snareTrack = new Track { Name = "Snare", Kind = TrackKind.Instrument };
        snareTrack.Instruments.Add(new InstrumentSlot(new BasicSamplerInstrument()));
        snareTrack.CommitInstruments();
        project.Tracks.Insert(project.Tracks.Count - 1, snareTrack);
        var snare = PatternTrackHelper.AddInstrumentRow(pattern, snareTrack);
        snare.Name = "Snare";
        snare.Order = 1;

        var kickSeq = pattern.GetOrCreateSequence(kick);
        kickSeq.Steps[0].Active = true;
        kickSeq.Steps[0].Note = 36;

        pattern.ReorderChannel(kick.Id, 1);

        Assert.Equal(1, kick.Order);
        Assert.Equal(0, snare.Order);
        Assert.Same(kickSeq, pattern.StepSequences.First(s => s.PatternChannelId == kick.Id));
    }

    private static Project BuildProject(out Pattern pattern, out Track patternTrack, out Track instrumentTrack)
    {
        var project = new Project { Name = "Test" };
        patternTrack = PatternTrackHelper.CreatePatternTrack(project);
        project.Tracks.Add(patternTrack);
        pattern = project.Patterns[0];

        instrumentTrack = new Track { Name = "Kick", Kind = TrackKind.Instrument };
        instrumentTrack.Instruments.Add(new InstrumentSlot(new BasicSamplerInstrument()));
        instrumentTrack.CommitInstruments();
        project.Tracks.Insert(0, instrumentTrack);
        project.Tracks.Add(new Track { Name = "Master", Kind = TrackKind.Master });
        return project;
    }

    private static PlaybackScheduleContext Context(Project project) => new()
    {
        Project = project,
        Tracks = project.Tracks,
        StartBeat = 0,
        Channels = 2,
        SampleRate = 48000,
        Bpm = 120
    };
}
