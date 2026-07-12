using System;
using System.Linq;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services.Implementation;
using Xunit;

namespace Ongenet.Core.Tests.Audio;

public sealed class SessionCaptureTests
{
    [Fact]
    public void LaunchDuringPlayback_LogsPendingCapture()
    {
        var (playback, transport, capture, sessionClip) = Setup();
        transport.StartBeat = 0;
        transport.Play();
        transport.NotifyPlayhead(2);

        playback.LaunchClip(sessionClip.Id);

        Assert.Equal(1, capture.PendingLaunchCount);
    }

    [Fact]
    public void Capture_CreatesClipAtLoggedBeat()
    {
        var (playback, transport, capture, sessionClip, track) = SetupWithTrack();
        transport.StartBeat = 0;
        transport.Play();
        transport.NotifyPlayhead(4);
        playback.LaunchClip(sessionClip.Id);
        Assert.Equal(1, capture.PendingLaunchCount);

        capture.Capture();
        Assert.Equal(0, capture.PendingLaunchCount);
        Assert.Single(track.Clips, c => c.StartBeat > 0);
        var captured = track.Clips.First(c => c.StartBeat > 0);
        Assert.Equal(4, captured.StartBeat, 3);
        Assert.Equal("Src", captured.Name);
    }

    [Fact]
    public void TransportStop_AutoMaterializesPendingLaunches()
    {
        var (playback, transport, capture, sessionClip, track) = SetupWithTrack();
        transport.Play();
        transport.NotifyPlayhead(8);
        playback.LaunchClip(sessionClip.Id);
        transport.Stop();

        Assert.Equal(0, capture.PendingLaunchCount);
        Assert.Equal(2, track.Clips.Count);
    }

    private static (PlaybackModeService Playback, TransportService Transport, SessionCaptureService Capture,
        SessionClip Clip) Setup()
    {
        var (playback, transport, capture, sessionClip, _) = SetupWithTrack();
        return (playback, transport, capture, sessionClip);
    }

    private static (PlaybackModeService Playback, TransportService Transport, SessionCaptureService Capture,
        SessionClip Clip, Track Track) SetupWithTrack()
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
            LengthBeats = 4
        };
        track.Clips.Add(src);

        var sessionClip = new SessionClip
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            SourceClipId = src.Id,
            LengthBeats = 4,
            LaunchMode = SessionLaunchMode.Trigger
        };
        projectSvc.Current.SessionClips.Add(sessionClip);
        return (playback, transport, capture, sessionClip, track);
    }
}
