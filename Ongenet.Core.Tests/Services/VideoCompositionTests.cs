using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Models.Media;
using Ongenet.Core.Services;
using Ongenet.VideoComposition.Rendering;

namespace Ongenet.Core.Tests.Services;

public sealed class VideoCompositionTimeMapperTests
{
    [Fact]
    public void ComputeLayerTimeSeconds_applies_offset_and_in_out()
    {
        var layer = new VideoLayer
        {
            OffsetSeconds = 2,
            InPointSeconds = 1,
            OutPointSeconds = 10
        };

        var t = VideoCompositionTimeMapper.ComputeLayerTimeSeconds(layer, 3, new Project());
        Assert.Equal(5, t, 3);
        Assert.True(VideoCompositionTimeMapper.IsLayerActiveAtTime(layer, t));
        Assert.False(VideoCompositionTimeMapper.IsLayerActiveAtTime(layer, 0.5));
    }

    [Fact]
    public void ComputeLayerTimeSeconds_uses_sync_clip_relative_time()
    {
        var clipId = Guid.NewGuid();
        var track = new Track { Name = "T1" };
        track.Clips.Add(new Clip { Id = clipId, StartBeat = 4, LengthBeats = 12 });
        var project = new Project();
        project.Tracks.Add(track);

        var layer = new VideoLayer { SyncClipId = clipId, OffsetSeconds = 1 };
        double BeatsToSeconds(Project _, double beats) => beats * 0.5;

        var t = VideoCompositionTimeMapper.ComputeLayerTimeSeconds(layer, 0, project, BeatsToSeconds, playheadBeats: 8);
        Assert.Equal(3, t, 3);
    }

    [Fact]
    public void ResolveExportFps_prefers_project_export_fps()
    {
        var project = new Project { VideoExportFps = 24 };
        project.VideoLayers.Add(new VideoLayer { Fps = 60 });
        Assert.Equal(24, VideoCompositionTimeMapper.ResolveExportFps(project));
    }

    [Fact]
    public void ResolveExportFps_uses_max_layer_fps_when_project_fps_unset()
    {
        var project = new Project();
        project.VideoLayers.Add(new VideoLayer { Fps = 30 });
        project.VideoLayers.Add(new VideoLayer { Fps = 24 });
        Assert.Equal(30, VideoCompositionTimeMapper.ResolveExportFps(project));
    }
}

public sealed class VideoTriggerEngineTests
{
    [Fact]
    public void Session_clip_end_fires_hide_trigger()
    {
        var layerId = Guid.NewGuid();
        var clipId = Guid.NewGuid();
        var project = new Project();
        project.VideoLayers.Add(new VideoLayer { Id = layerId, Opacity = 1 });
        project.VideoTriggers.Add(new VideoTrigger
        {
            TargetLayerId = layerId,
            Source = VideoTriggerSource.SessionClip,
            ClipId = clipId,
            Moment = VideoTriggerMoment.ClipEnd,
            Action = VideoTriggerAction.Hide
        });

        var engine = new VideoTriggerEngine();
        engine.Reset(project);
        engine.OnSessionClipEvent(project, clipId, VideoTriggerMoment.ClipStart);
        Assert.True(engine.Runtime.GetOpacity(layerId) > 0.01);

        engine.OnSessionClipEvent(project, clipId, VideoTriggerMoment.ClipEnd);
        Assert.True(engine.Runtime.GetOpacity(layerId) <= 0.01);
    }
}

public sealed class OfflineVideoAudioScopeTests
{
    [Fact]
    public void CaptureLatest_advances_with_SetTime()
    {
        var trackId = Guid.NewGuid();
        var samples = new float[48000];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = i / 48000f;
        var buffer = new Ongenet.Core.Audio.Files.AudioSampleBuffer(samples, 1, 48000);
        var scope = new OfflineVideoAudioScope(new Dictionary<Guid, Ongenet.Core.Audio.Files.AudioSampleBuffer>
        {
            [trackId] = buffer
        });
        var dest = new float[8];

        scope.SetTime(0);
        var atStart = scope.CaptureLatest(trackId, dest);
        Assert.True(atStart > 0);
        Assert.True(dest[atStart - 1] < dest[0] || atStart == 1);

        scope.SetTime(0.5);
        var atMid = scope.CaptureLatest(trackId, dest);
        Assert.Equal(8, atMid);
        Assert.True(dest[atMid - 1] > dest[0]);
    }

    [Fact]
    public void CaptureLatest_returns_zero_for_unknown_track()
    {
        var scope = new OfflineVideoAudioScope(new Dictionary<Guid, Ongenet.Core.Audio.Files.AudioSampleBuffer>());
        Assert.Equal(0, scope.CaptureLatest(Guid.NewGuid(), new float[16]));
    }
}
