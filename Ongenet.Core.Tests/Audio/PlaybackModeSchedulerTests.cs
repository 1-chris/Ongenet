using System;
using System.Linq;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Implementation;
using Ongenet.Core.Services.Interfaces;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class PlaybackModeSchedulerTests
{
    [Fact]
    public void QueueClip_FiresOnQuantizeBoundary()
    {
        var (playback, transport, sessionClip) = SetupSessionClip(SessionLaunchMode.Trigger);
        transport.StartBeat = 1;
        transport.Play();
        transport.NotifyPlayhead(1);
        playback.LaunchQuantizeBeats = 4;

        playback.QueueClip(sessionClip.Id);
        Assert.True(sessionClip.IsQueued);
        Assert.DoesNotContain(sessionClip.Id, playback.ActiveSessionClipIds);

        playback.ProcessPlayhead(3.5);
        Assert.DoesNotContain(sessionClip.Id, playback.ActiveSessionClipIds);

        playback.ProcessPlayhead(4.0);
        Assert.Contains(sessionClip.Id, playback.ActiveSessionClipIds);
        Assert.False(sessionClip.IsQueued);
    }

    [Fact]
    public void GateClip_StopsWhenReleased()
    {
        var (playback, _, sessionClip) = SetupSessionClip(SessionLaunchMode.Gate);
        playback.GateClip(sessionClip.Id, held: true);
        Assert.Contains(sessionClip.Id, playback.ActiveSessionClipIds);

        playback.GateClip(sessionClip.Id, held: false);
        Assert.DoesNotContain(sessionClip.Id, playback.ActiveSessionClipIds);
    }

    [Fact]
    public void LaunchClip_ToggleStopsOnSecondLaunch()
    {
        var (playback, _, sessionClip) = SetupSessionClip(SessionLaunchMode.Toggle);
        playback.LaunchClip(sessionClip.Id);
        Assert.Contains(sessionClip.Id, playback.ActiveSessionClipIds);

        playback.LaunchClip(sessionClip.Id);
        Assert.DoesNotContain(sessionClip.Id, playback.ActiveSessionClipIds);
    }

    private static (PlaybackModeService Playback, TransportService Transport, SessionClip Clip) SetupSessionClip(
        SessionLaunchMode mode)
    {
        var instruments = new InstrumentRegistry();
        var projectSvc = new ProjectService(instruments);
        var transport = new TransportService();
        var events = new EventAggregator();
        var capture = new SessionCaptureService(projectSvc, transport, events);
        var playback = new PlaybackModeService(projectSvc, transport, capture);

        var track = projectSvc.Current.Tracks.First(t => t.Kind == TrackKind.Instrument);
        var src = new Clip
        {
            Id = Guid.NewGuid(),
            Name = "Src",
            IsAudio = false,
            LengthBeats = 4,
            Notes = { new MidiNote { Note = 36, StartBeat = 0, LengthBeats = 1, Velocity = 1f } }
        };
        track.Clips.Add(src);

        var sessionClip = new SessionClip
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            SourceClipId = src.Id,
            LengthBeats = 4,
            LaunchMode = mode
        };
        projectSvc.Current.SessionClips.Add(sessionClip);
        return (playback, transport, sessionClip);
    }
}
