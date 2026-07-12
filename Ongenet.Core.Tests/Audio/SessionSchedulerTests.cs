using System;
using System.Collections.Generic;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Interfaces;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class SessionSchedulerTests
{
    private const int SampleRate = 44100;
    private const int Channels = 2;

    [Fact]
    public void LaunchClip_SchedulesSourceMidiNotes()
    {
        var track = new Track { Id = Guid.NewGuid(), Name = "Synth", Kind = TrackKind.Instrument };
        var slot = new InstrumentSlot(new OscillatorInstrument()) { Enabled = true };
        track.Instruments.Add(slot);
        track.CommitInstruments();

        var src = new Clip
        {
            Id = Guid.NewGuid(),
            Name = "Pattern",
            IsAudio = false,
            StartBeat = 0,
            LengthBeats = 4,
            Notes = { new MidiNote { Note = 60, StartBeat = 0, LengthBeats = 1, Velocity = 0.8f } }
        };
        track.Clips.Add(src);

        var sessionClip = new SessionClip
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            SourceClipId = src.Id,
            LengthBeats = 4
        };

        var launches = new Dictionary<Guid, SessionClipLaunchState>
        {
            [sessionClip.Id] = new SessionClipLaunchState { Clip = sessionClip, LaunchBeat = 0 }
        };

        var schedule = new SessionScheduler(new[] { sessionClip }, launches).Build(Context(new[] { track }));
        Assert.Contains(schedule.Notes, n => n.Note == 60 && n.OnBeat == 0);
    }

    [Fact]
    public void HasClipEnded_IsFalseForRepeatMode()
    {
        var clip = new SessionClip { LaunchMode = SessionLaunchMode.Repeat, LengthBeats = 4 };
        Assert.False(SessionScheduler.HasClipEnded(clip, launchBeat: 0, playheadBeat: 100));
    }

    private static PlaybackScheduleContext Context(IReadOnlyList<Track> tracks)
        => new()
        {
            Project = new Project { Tempo = new Tempo(120), TimeSignature = TimeSignature.FourFour, BarCount = 4 },
            Tracks = tracks,
            StartBeat = 0,
            SampleRate = SampleRate,
            Channels = Channels,
            Bpm = 120
        };
}
