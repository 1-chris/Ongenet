using System;
using System.IO;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Scheduling;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;
using Xunit;

namespace Ongenet.Core.Tests.Persistence;

public sealed class ProjectFileV4Tests
{
    [Fact]
    public void V4Extensions_RoundTrip()
    {
        var project = new Project { Name = "V4 Test" };
        var track = new Track { Name = "Drums", Kind = TrackKind.Audio };
        project.Tracks.Add(track);

        var clip = new Clip { Name = "Loop", IsAudio = true, StartBeat = 0, LengthBeats = 4, WarpMode = WarpMode.Complex };
        clip.WarpMarkers.Add(new WarpMarker { SourceSeconds = 0, BeatPosition = 0 });
        clip.WarpMarkers.Add(new WarpMarker { SourceSeconds = 1, BeatPosition = 2 });
        track.Clips.Add(clip);

        track.Sends.Add(new TrackSend { TargetTrackId = Guid.NewGuid(), Level = 0.5, PreFader = true });
        track.TakeLanes.Add(new TakeLane
        {
            Name = "Take 1",
            Takes = { new Take { ClipId = clip.Id, StartBeat = 0, LengthBeats = 4, IsSelected = true } }
        });

        var pat = new Pattern { Name = "Pat 1", LengthBeats = 4 };
        var ch = new PatternChannel { TrackId = track.Id, Name = "Kick" };
        pat.Channels.Add(ch);
        pat.StepSequences.Add(new StepSequence
        {
            PatternChannelId = ch.Id,
            StepCount = 16,
            Steps = { new StepData { Active = true, Note = 36, Velocity = 0.9f } }
        });
        project.Patterns.Add(pat);
        project.PatternClips.Add(new PatternClip { PatternId = pat.Id, TrackId = track.Id, StartBeat = 0, LengthBeats = 4 });

        project.SessionClips.Add(new SessionClip
        {
            TrackId = track.Id,
            SceneIndex = 0,
            Name = "Scene Clip",
            SourceClipId = clip.Id,
            LaunchMode = SessionLaunchMode.Gate,
            FollowAction = FollowAction.PlayNext,
            LaunchQuantizeBeats = 0.25
        });

        project.MultiOutputRoutes.Add(new MultiOutputRoute
        {
            SourceTrackId = track.Id,
            SlotIndex = 0,
            PluginOutputBus = 1,
            DestinationTrackId = Guid.NewGuid(),
            Level = 0.8
        });

        project.Mpe.Enabled = true;
        project.ActiveGroove = new GrooveTemplate { Name = "Swing", SwingAmount = 0.6, Division = 16 };
        project.DrumMaps.Add(new DrumMap
        {
            Name = "Kit",
            Entries = { new DrumMapEntry { Note = 36, Label = "Kick", VelocityScale = 1.2f } }
        });

        project.VideoTracks.Add(new VideoTrack
        {
            FilePath = "/tmp/test.mp4",
            OffsetSeconds = 1.5,
            Fps = 30,
            Muted = true
        });
        track.SurroundWidth = 0.75;

        using var ms = new MemoryStream();
        ProjectFile.Save(project, ms, "test", 0, 8, 0);
        ms.Position = 0;
        var loaded = ProjectFile.Load(ms, new InstrumentRegistry(), new EffectRegistry()).Project;

        Assert.Equal("V4 Test", loaded.Name);
        Assert.Single(loaded.Patterns);
        Assert.Equal("Pat 1", loaded.Patterns[0].Name);
        Assert.Single(loaded.Patterns[0].StepSequences);
        Assert.True(loaded.Patterns[0].StepSequences[0].Steps[0].Active);
        Assert.Single(loaded.PatternClips);
        Assert.Single(loaded.SessionClips);
        Assert.Equal(SessionLaunchMode.Gate, loaded.SessionClips[0].LaunchMode);
        Assert.Equal(FollowAction.PlayNext, loaded.SessionClips[0].FollowAction);
        Assert.Equal(0.25, loaded.SessionClips[0].LaunchQuantizeBeats);
        Assert.Single(loaded.MultiOutputRoutes);
        Assert.True(loaded.Mpe.Enabled);
        Assert.NotNull(loaded.ActiveGroove);
        Assert.Single(loaded.DrumMaps);
        Assert.Single(loaded.Tracks[0].TakeLanes);
        Assert.Equal(WarpMode.Complex, loaded.Tracks[0].Clips[0].WarpMode);
        Assert.Equal(2, loaded.Tracks[0].Clips[0].WarpMarkers.Count);
        Assert.Single(loaded.VideoTracks);
        Assert.Equal("/tmp/test.mp4", loaded.VideoTracks[0].FilePath);
        Assert.Equal(1.5, loaded.VideoTracks[0].OffsetSeconds);
        Assert.True(loaded.VideoTracks[0].Muted);
        Assert.Equal(0.75, loaded.Tracks[0].SurroundWidth);
    }
}
